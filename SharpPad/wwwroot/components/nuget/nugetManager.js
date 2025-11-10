const SEARCH_DEBOUNCE_MS = 350;

const DEFAULT_PACKAGE_SOURCES = [
    {
        key: "nuget",
        name: "NuGet.org",
        url: "https://packages.nuget.org/api/v2/package",
        searchUrl: "https://azuresearch-usnc.nuget.org/query",
        apiUrl: "https://api.nuget.org/v3-flatcontainer",
        isDefault: true,
        isCustom: false
    },
    {
        key: "azure",
        name: "Azure CDN",
        url: "https://nuget.cdn.azure.cn/api/v2/package",
        searchUrl: "https://azuresearch-usnc.nuget.org/query",
        apiUrl: "https://nuget.cdn.azure.cn/v3-flatcontainer",
        isDefault: false,
        isCustom: false
    },
    {
        key: "huawei",
        name: "Huawei Cloud",
        url: "https://mirrors.huaweicloud.com/repository/nuget/v2/package",
        searchUrl: "https://azuresearch-usnc.nuget.org/query",
        apiUrl: "https://mirrors.huaweicloud.com/repository/nuget/v3-flatcontainer",
        isDefault: false,
        isCustom: false
    }
];

const FALLBACK_SOURCE_KEY = "nuget";

const PACKAGE_SOURCE_ENDPOINTS = {
    list: "/api/PackageSource/list",
    add: "/api/PackageSource/custom",
    remove: (key) => `/api/PackageSource/custom/${encodeURIComponent(key)}`
};

const escapeHtml = (value = "") => {
    const text = value === null || value === undefined ? "" : String(value);
    return text
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");
};

const formatDownloads = (total = 0) => {
    if (total >= 1_000_000_000) {
        return `${(total / 1_000_000_000).toFixed(1)}B`;
    }
    if (total >= 1_000_000) {
        return `${(total / 1_000_000).toFixed(1)}M`;
    }
    if (total >= 1_000) {
        return `${(total / 1_000).toFixed(1)}K`;
    }
    return `${total}`;
};

const parseVersion = (value = "0") => {
    const [main, pre = ""] = value.split("-");
    const parts = main.split(".").map(part => Number.parseInt(part, 10) || 0);
    return { parts, pre };
};

const compareVersions = (left = "0", right = "0") => {
    const leftParts = parseVersion(left);
    const rightParts = parseVersion(right);
    const length = Math.max(leftParts.parts.length, rightParts.parts.length);
    for (let index = 0; index < length; index += 1) {
        const diff = (leftParts.parts[index] || 0) - (rightParts.parts[index] || 0);
        if (diff !== 0) {
            return diff;
        }
    }
    if (!leftParts.pre && !rightParts.pre) {
        return 0;
    }
    if (!leftParts.pre) {
        return 1;
    }
    if (!rightParts.pre) {
        return -1;
    }
    return leftParts.pre.localeCompare(rightParts.pre);
};

const isPrereleaseVersion = (value = "0") => Boolean(parseVersion(value).pre);

const cssEscape = (value) => {
    if (window.CSS && typeof window.CSS.escape === "function") {
        return window.CSS.escape(value);
    }
    return value.replace(/([^a-zA-Z0-9_-])/g, "\\$1");
};

const buildLink = (href, label) => `<a href="${href}" target="_blank" rel="noopener noreferrer">${escapeHtml(label)}</a>`;

export class NugetManager {
    constructor({ sendRequest, notify }) {
        this.sendRequest = sendRequest;
        this.notify = typeof notify === "function" ? notify : (() => {});

        this.dialog = document.getElementById("nugetConfigDialog");
        this.searchInput = document.getElementById("nugetSearchInput");
        this.searchButton = document.getElementById("nugetSearchButton");
        this.includePrerelease = document.getElementById("nugetIncludePrerelease");
        this.packageSourceSelect = document.getElementById("nugetPackageSource");
        this.searchResultsEl = document.getElementById("nugetSearchResults");
        this.installedListEl = document.getElementById("nugetInstalledList");
        this.updatesListEl = document.getElementById("nugetUpdatesList");
        this.detailsPanel = document.getElementById("nugetDetailsPanel");
        this.tabButtons = Array.from(document.querySelectorAll(".nuget-tab"));
        this.panels = {
            browse: document.getElementById("nugetBrowsePanel"),
            installed: document.getElementById("nugetInstalledPanel"),
            updates: document.getElementById("nugetUpdatesPanel")
        };

        this.manageSourcesButton = document.getElementById("nugetManageSourcesButton");
        this.sourceModal = document.getElementById("nugetSourceModal");
        this.sourceModalCloseButton = document.getElementById("nugetSourceModalClose");
        this.sourceListEl = document.getElementById("nugetSourceList");
        this.sourceForm = document.getElementById("nugetSourceForm");
        this.sourceSubmitButton = document.getElementById("nugetSourceSubmit");
        this.sourceKeyInput = document.getElementById("nugetSourceKey");
        this.sourceNameInput = document.getElementById("nugetSourceName");
        this.sourceUrlInput = document.getElementById("nugetSourceUrl");
        this.sourceSearchUrlInput = document.getElementById("nugetSourceSearchUrl");
        this.sourceApiUrlInput = document.getElementById("nugetSourceApiUrl");

        this.packageSources = new Map(DEFAULT_PACKAGE_SOURCES.map((source) => [source.key, { ...source }]));
        this.defaultSourceKey = DEFAULT_PACKAGE_SOURCES.find((source) => source.isDefault)?.key || FALLBACK_SOURCE_KEY;
        this.currentSourceKey = this.defaultSourceKey;
        this.isLoadingSources = false;
        this.activeTab = "browse";
        this.currentFile = null;
        this.pendingUpdates = [];
        this.packageCache = new Map();
        this.searchCache = new Map();
        this.latestVersionCache = new Map();
        this.detailRequestToken = 0;
        this.searchDebounceTimer = null;
        this.currentSearchAbort = null;
        this.lastSearchResults = [];
    }
    async initialize() {
        if (!this.dialog) {
            return;
        }

        await this.refreshPackageSources({ suppressNotifications: true });
        this.updateSearchPlaceholder();

        this.tabButtons.forEach((button) => {
            button.addEventListener("click", () => {
                this.switchTab(button.dataset.tab);
            });
        });

        if (this.searchInput) {
            this.searchInput.addEventListener("input", () => {
                clearTimeout(this.searchDebounceTimer);
                this.searchDebounceTimer = setTimeout(() => {
                    if (this.activeTab === "browse") {
                        this.performSearch();
                    }
                }, SEARCH_DEBOUNCE_MS);
            });

            this.searchInput.addEventListener("keydown", (event) => {
                if (event.key === "Enter") {
                    clearTimeout(this.searchDebounceTimer);
                    this.performSearch(true);
                }
            });
        }

        if (this.includePrerelease) {
            this.includePrerelease.addEventListener("change", () => {
                if (this.activeTab === "browse") {
                    this.performSearch(true);
                }
            });
        }

        if (this.packageSourceSelect) {
            this.packageSourceSelect.addEventListener("change", () => {
                this.currentSourceKey = (this.packageSourceSelect.value || FALLBACK_SOURCE_KEY).toLowerCase();
                this.resetSourceCaches();
                this.updateSearchPlaceholder();

                if (this.activeTab === "browse") {
                    this.performSearch(true);
                } else if (this.activeTab === "updates") {
                    this.refreshUpdates({ background: true });
                }
            });
        }

        if (this.searchButton) {
            this.searchButton.addEventListener("click", () => {
                this.performSearch(true);
            });
        }

        if (this.manageSourcesButton) {
            this.manageSourcesButton.addEventListener("click", () => {
                this.openSourceManager();
            });
        }

        if (this.sourceModalCloseButton) {
            this.sourceModalCloseButton.addEventListener("click", () => {
                this.closeSourceManager();
            });
        }

        if (this.sourceModal) {
            // 移除点击背景关闭功能 - 防止误触
            // this.sourceModal.addEventListener("click", (event) => {
            //     if (event.target === this.sourceModal) {
            //         this.closeSourceManager();
            //     }
            // });
        }

        if (this.sourceForm) {
            this.sourceForm.addEventListener("submit", async (event) => {
                event.preventDefault();
                await this.handleCustomSourceSubmit();
            });
        }

        // 移除点击背景关闭功能 - 防止误触
        // this.dialog.addEventListener("click", (event) => {
        //     if (event.target === this.dialog) {
        //         this.close();
        //     }
        // });

        document.addEventListener("keydown", (event) => {
            if (event.key === "Escape") {
                if (this.sourceModal && this.sourceModal.style.display === "block") {
                    this.closeSourceManager();
                    event.preventDefault();
                    return;
                }

                if (this.dialog.style.display === "block") {
                    this.close();
                }
            }
        });
    }
    async refreshPackageSources({ suppressNotifications = false } = {}) {
        this.isLoadingSources = true;
        const previousKey = (this.packageSourceSelect?.value || this.currentSourceKey || this.defaultSourceKey || FALLBACK_SOURCE_KEY).toLowerCase();
        let fetchedSources = null;

        try {
            const response = await fetch(PACKAGE_SOURCE_ENDPOINTS.list);
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }
            const payload = await response.json();
            const data = payload?.data ?? payload?.Data;
            if (Array.isArray(data)) {
                fetchedSources = data;
            }
        } catch (error) {
            console.error('加载包源失败', error);
            if (!suppressNotifications) {
                this.notify(`加载包源失败：${error.message || '未知错误'}`, 'error');
            }
        }

        this.packageSources.clear();
        DEFAULT_PACKAGE_SOURCES.forEach((source) => {
            this.packageSources.set(source.key, { ...source });
        });

        if (Array.isArray(fetchedSources)) {
            fetchedSources.forEach((raw) => {
                const normalized = this.normalizeSource(raw);
                if (normalized) {
                    this.packageSources.set(normalized.key, normalized);
                }
            });
        }

        const defaultCandidate = Array.from(this.packageSources.values()).find((source) => source.isDefault);
        this.defaultSourceKey = defaultCandidate?.key || DEFAULT_PACKAGE_SOURCES.find((source) => source.isDefault)?.key || FALLBACK_SOURCE_KEY;

        this.populatePackageSourceSelect(previousKey);
        this.resetSourceCaches();
        this.isLoadingSources = false;

        if (this.sourceModal && this.sourceModal.style.display === 'block') {
            this.renderSourceList();
        }
    }

    normalizeSource(raw) {
        if (!raw) {
            return null;
        }

        const keyValue = (raw.key ?? raw.Key ?? '').toString().trim().toLowerCase();
        if (!keyValue) {
            return null;
        }

        return {
            key: keyValue,
            name: raw.name ?? raw.Name ?? keyValue,
            url: raw.url ?? raw.Url ?? '',
            searchUrl: raw.searchUrl ?? raw.SearchUrl ?? '',
            apiUrl: raw.apiUrl ?? raw.ApiUrl ?? '',
            isDefault: Boolean(raw.isDefault ?? raw.IsDefault ?? false),
            isCustom: Boolean(raw.isCustom ?? raw.IsCustom ?? false)
        };
    }

    populatePackageSourceSelect(preferredKey) {
        if (!this.packageSourceSelect) {
            if (preferredKey) {
                this.currentSourceKey = preferredKey.toLowerCase();
            }
            return;
        }

        const sources = Array.from(this.packageSources.values());
        sources.sort((left, right) => {
            if (left.key === this.defaultSourceKey && right.key !== this.defaultSourceKey) {
                return -1;
            }
            if (left.key !== this.defaultSourceKey && right.key === this.defaultSourceKey) {
                return 1;
            }
            return left.name.localeCompare(right.name, 'zh-Hans', { sensitivity: 'base' });
        });

        this.packageSourceSelect.innerHTML = '';
        sources.forEach((source) => {
            const option = document.createElement('option');
            option.value = source.key;
            option.textContent = source.name;
            option.dataset.custom = source.isCustom ? 'true' : 'false';
            this.packageSourceSelect.appendChild(option);
        });

        let resolvedKey = preferredKey && this.packageSources.has(preferredKey)
            ? preferredKey
            : (this.packageSources.has(this.currentSourceKey) ? this.currentSourceKey : this.defaultSourceKey);

        if (!resolvedKey && sources.length > 0) {
            resolvedKey = sources[0].key;
        }

        if (resolvedKey) {
            this.packageSourceSelect.value = resolvedKey;
        }

        this.currentSourceKey = (this.packageSourceSelect.value || this.defaultSourceKey || FALLBACK_SOURCE_KEY).toLowerCase();
        this.updateSearchPlaceholder();
    }

    updateSearchPlaceholder() {
        if (!this.searchInput) {
            return;
        }
        const activeSource = this.getActiveSource();
        const sourceName = activeSource?.name || 'NuGet';
        this.searchInput.placeholder = `搜索 ${sourceName} 上的包 (例如：Newtonsoft.Json)`;
    }

    resetSourceCaches() {
        if (this.currentSearchAbort) {
            this.currentSearchAbort.abort();
            this.currentSearchAbort = null;
        }
        this.packageCache.clear();
        this.searchCache.clear();
        this.latestVersionCache.clear();
        this.detailRequestToken += 1;
        this.lastSearchResults = [];
        this.lastQuery = null;
    }

    openSourceManager() {
        if (!this.sourceModal) {
            return;
        }
        this.renderSourceList();
        this.resetSourceForm();
        this.toggleSourceFormDisabled(false);
        this.sourceModal.style.display = 'block';
        if (this.sourceKeyInput) {
            setTimeout(() => this.sourceKeyInput.focus(), 0);
        }
    }

    closeSourceManager() {
        if (!this.sourceModal) {
            return;
        }
        this.sourceModal.style.display = 'none';
        this.toggleSourceFormDisabled(false);
    }

    renderSourceList() {
        if (!this.sourceListEl) {
            return;
        }

        const sources = Array.from(this.packageSources.values());
        if (sources.length === 0) {
            this.sourceListEl.innerHTML = '<div class="nuget-source-empty">暂无可用包源</div>';
            return;
        }

        const selectedKey = this.getSelectedPackageSource();
        sources.sort((left, right) => left.name.localeCompare(right.name, 'zh-Hans', { sensitivity: 'base' }));

        const markup = sources.map((source) => {
            const tags = [];
            if (source.isDefault) {
                tags.push('<span class="nuget-source-tag default">默认</span>');
            }
            if (source.isCustom) {
                tags.push('<span class="nuget-source-tag custom">自定义</span>');
            }
            if (source.key === selectedKey) {
                tags.push('<span class="nuget-source-tag active">当前</span>');
            }

            const urlDisplay = source.url || '未配置';
            const searchDisplay = source.searchUrl || '未配置';
            const apiDisplay = source.apiUrl || '未配置';
            const removeButton = source.isCustom
                ? `<button class="nuget-source-remove" data-key="${escapeHtml(source.key)}">移除</button>`
                : '';

            return `
                <div class="nuget-source-item">
                    <div class="nuget-source-info">
                        <div class="nuget-source-title">${escapeHtml(source.name)}
                            <div class="nuget-source-tags">${tags.join('')}</div>
                        </div>
                        <div class="nuget-source-meta">标识：${escapeHtml(source.key)}</div>
                        <div class="nuget-source-meta">包下载：${escapeHtml(urlDisplay)}</div>
                        <div class="nuget-source-meta">搜索：${escapeHtml(searchDisplay)}</div>
                        <div class="nuget-source-meta">版本查询：${escapeHtml(apiDisplay)}</div>
                    </div>
                    <div class="nuget-source-actions">${removeButton}</div>
                </div>
            `;
        }).join('');

        this.sourceListEl.innerHTML = markup;
        this.sourceListEl.querySelectorAll('.nuget-source-remove').forEach((button) => {
            button.addEventListener('click', () => {
                const targetKey = button.getAttribute('data-key');
                this.removeCustomSource(targetKey);
            });
        });
    }

    resetSourceForm() {
        if (this.sourceForm) {
            this.sourceForm.reset();
        }
        if (this.sourceSubmitButton) {
            this.sourceSubmitButton.disabled = false;
            this.sourceSubmitButton.textContent = '保存包源';
        }
    }

    toggleSourceFormDisabled(disabled, pendingLabel = '保存中...') {
        if (!this.sourceForm) {
            return;
        }

        Array.from(this.sourceForm.elements).forEach((element) => {
            element.disabled = disabled;
        });

        if (this.sourceSubmitButton) {
            this.sourceSubmitButton.disabled = disabled;
            this.sourceSubmitButton.textContent = disabled ? pendingLabel : '保存包源';
        }
    }

    async handleCustomSourceSubmit() {
        if (!this.sourceForm) {
            return;
        }

        const key = (this.sourceKeyInput?.value || '').trim().toLowerCase();
        const name = (this.sourceNameInput?.value || '').trim();
        const url = (this.sourceUrlInput?.value || '').trim();
        const searchUrl = (this.sourceSearchUrlInput?.value || '').trim();
        const apiUrl = (this.sourceApiUrlInput?.value || '').trim();

        if (!key) {
            this.notify('请填写包源标识', 'error');
            this.sourceKeyInput?.focus();
            return;
        }

        if (!/^[a-z0-9_-]+$/.test(key)) {
            this.notify('包源标识仅支持字母、数字、短横线和下划线', 'error');
            this.sourceKeyInput?.focus();
            return;
        }

        if (!url) {
            this.notify('请填写包下载地址', 'error');
            this.sourceUrlInput?.focus();
            return;
        }

        const payload = {
            Key: key,
            Name: name || key,
            Url: url,
            SearchUrl: searchUrl || null,
            ApiUrl: apiUrl || null
        };

        this.toggleSourceFormDisabled(true);

        try {
            const response = await fetch(PACKAGE_SOURCE_ENDPOINTS.add, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(payload)
            });

            const result = await response.json().catch(() => ({}));
            if (!response.ok || result?.code !== 0) {
                throw new Error(result?.message || `HTTP ${response.status}`);
            }

            this.notify(`已保存包源 ${payload.Name}`, 'success');
            await this.refreshPackageSources({ suppressNotifications: true });

            if (this.packageSourceSelect) {
                this.packageSourceSelect.value = key;
                this.packageSourceSelect.dispatchEvent(new Event('change'));
            }

            this.renderSourceList();
            this.resetSourceForm();
        } catch (error) {
            console.error('添加包源失败', error);
            this.notify(`添加包源失败：${error.message || '未知错误'}`, 'error');
        } finally {
            this.toggleSourceFormDisabled(false);
        }
    }

    async removeCustomSource(key) {
        if (!key) {
            return;
        }

        const normalizedKey = key.toLowerCase();
        const targetSource = this.packageSources.get(normalizedKey);
        if (!targetSource?.isCustom) {
            this.notify('只能移除自定义包源', 'error');
            return;
        }

        try {
            const response = await fetch(PACKAGE_SOURCE_ENDPOINTS.remove(normalizedKey), {
                method: 'DELETE'
            });
            const result = await response.json().catch(() => ({}));
            if (!response.ok || result?.code !== 0) {
                throw new Error(result?.message || `HTTP ${response.status}`);
            }

            const wasSelected = this.getSelectedPackageSource() === normalizedKey;
            this.notify(`已移除包源 ${targetSource.name}`, 'success');
            await this.refreshPackageSources({ suppressNotifications: true });

            if (wasSelected && this.packageSourceSelect) {
                const fallbackKey = this.packageSources.has(this.defaultSourceKey)
                    ? this.defaultSourceKey
                    : (this.packageSources.keys().next().value || FALLBACK_SOURCE_KEY);
                this.packageSourceSelect.value = fallbackKey;
                this.packageSourceSelect.dispatchEvent(new Event('change'));
            } else {
                this.updateSearchPlaceholder();
            }

            this.renderSourceList();
        } catch (error) {
            console.error('移除包源失败', error);
            this.notify(`移除包源失败：${error.message || '未知错误'}`, 'error');
        }
    }

    composeSourceCacheKey(sourceKey, packageId) {
        return `${(sourceKey || '').toLowerCase()}::${(packageId || '').toLowerCase()}`;
    }

    getActiveSource(sourceKey) {
        const key = (sourceKey || this.getSelectedPackageSource() || FALLBACK_SOURCE_KEY).toLowerCase();
        return this.packageSources.get(key) || this.packageSources.get(this.defaultSourceKey) || DEFAULT_PACKAGE_SOURCES[0];
    }

    open(file) {
        if (!file) {
            return;
        }

        this.currentFile = file;
        if (!this.currentFile.nugetConfig) {
            this.currentFile.nugetConfig = { packages: [] };
            this.persistFile();
        }

        this.dialog.style.display = "block";
        this.switchTab("browse", { skipReload: true });
        this.renderInstalled();
        this.refreshUpdates({ background: true });

        if (this.searchInput) {
            this.searchInput.focus();
        }

        if (this.searchInput && this.searchInput.value.trim()) {
            this.performSearch(true);
        } else {
            this.clearDetailsPanel();
            this.highlightSelection(null);
        }
    }

    close() {
        this.dialog.style.display = "none";
        this.currentFile = null;
        this.highlightSelection(null);
        this.clearDetailsPanel();
        window.currentFileId = null;
    }

    getCurrentFile() {
        return this.currentFile;
    }

    getSelectedPackageSource() {
        const value = (this.packageSourceSelect?.value || this.currentSourceKey || this.defaultSourceKey || FALLBACK_SOURCE_KEY).toLowerCase();
        this.currentSourceKey = value;
        return value;
    }

    switchTab(tabName, options = {}) {
        if (!this.panels[tabName]) {
            return;
        }

        this.activeTab = tabName;
        this.tabButtons.forEach((button) => {
            button.classList.toggle("active", button.dataset.tab === tabName);
        });

        Object.entries(this.panels).forEach(([name, panel]) => {
            panel.classList.toggle("active", name === tabName);
        });

        if (!options.skipReload) {
            if (tabName === "installed") {
                this.renderInstalled();
            }
            if (tabName === "updates") {
                this.refreshUpdates();
            }
            if (tabName === "browse" && this.searchInput && this.searchInput.value.trim()) {
                this.performSearch(true);
            }
        }
    }
    async performSearch(force = false) {
        if (!this.searchResultsEl || !this.searchInput) {
            return;
        }

        const query = this.searchInput.value.trim();
        if (!query) {
            this.searchResultsEl.innerHTML = '<div class="nuget-empty">输入关键字开始搜索 NuGet 包</div>';
            this.lastSearchResults = [];
            this.highlightSelection(null);
            this.clearDetailsPanel();
            return;
        }

        if (!force && query === this.lastQuery) {
            return;
        }

        this.lastQuery = query;
        this.searchResultsEl.innerHTML = '<div class="nuget-loading">正在搜索包...</div>';
        this.highlightSelection(null);
        this.searchCache.clear();

        if (this.currentSearchAbort) {
            this.currentSearchAbort.abort();
        }

        this.currentSearchAbort = new AbortController();
        const sourceKey = this.getSelectedPackageSource();
        const activeSource = this.getActiveSource(sourceKey);
        const searchEndpoint = activeSource?.searchUrl || DEFAULT_PACKAGE_SOURCES[0].searchUrl;

        if (!searchEndpoint) {
            this.searchResultsEl.innerHTML = '<div class="nuget-error">当前包源缺少搜索地址</div>';
            return;
        }

        let url;
        try {
            url = new URL(searchEndpoint);
        } catch (error) {
            console.error('Invalid search endpoint', error);
            this.searchResultsEl.innerHTML = '<div class="nuget-error">当前包源的搜索地址无效</div>';
            return;
        }

        url.searchParams.set("q", query);
        url.searchParams.set("skip", "0");
        url.searchParams.set("take", "20");
        url.searchParams.set("prerelease", this.includePrerelease?.checked ? "true" : "false");

        try {
            const response = await fetch(url.toString(), { signal: this.currentSearchAbort.signal });
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }
            const payload = await response.json();
            const results = Array.isArray(payload.data) ? payload.data : [];
            this.lastSearchResults = results;
            this.renderBrowseResults(results);
        } catch (error) {
            if (error.name === "AbortError") {
                return;
            }
            console.error("NuGet search error", error);
            this.searchResultsEl.innerHTML = `<div class="nuget-error">搜索失败：${escapeHtml(error.message || "未知错误")}</div>`;
        }
    }

    renderBrowseResults(results) {
        if (!this.searchResultsEl) {
            return;
        }

        if (!Array.isArray(results) || results.length === 0) {
            this.searchResultsEl.innerHTML = '<div class="nuget-empty">未找到匹配的 NuGet 包</div>';
            return;
        }

        const fragments = results.map((pkg) => {
            const installed = this.getInstalledPackage(pkg.id);
            const updateInfo = this.pendingUpdates.find((item) => item.id.toLowerCase() === pkg.id.toLowerCase());
            const badges = [];
            if (installed) {
                badges.push('<span class="nuget-installed-badge">已安装</span>');
            }
            if (updateInfo) {
                badges.push('<span class="nuget-status-label update">可更新</span>');
            }
            this.searchCache.set(pkg.id.toLowerCase(), pkg);
            return `
                <div class="nuget-result-item" data-source="browse" data-package-id="${escapeHtml(pkg.id)}">
                    <div class="nuget-result-header">
                        <div class="nuget-package-id">${escapeHtml(pkg.id)}</div>
                        <div class="nuget-package-version">最新 ${escapeHtml(pkg.version)}</div>
                    </div>
                    <div class="nuget-package-desc">${escapeHtml(pkg.description || pkg.summary || "暂无描述")}</div>
                    <div class="nuget-meta">
                        <span>作者 ${escapeHtml(pkg.authors || "未知")}</span>
                        <span>下载 ${formatDownloads(pkg.totalDownloads || 0)}</span>
                        ${badges.join("")}
                    </div>
                </div>
            `;
        }).join("");

        this.searchResultsEl.innerHTML = fragments;
        this.searchResultsEl.querySelectorAll(".nuget-result-item").forEach((element) => {
            element.addEventListener("click", () => {
                const packageId = element.dataset.packageId;
                const cached = this.searchCache.get(packageId.toLowerCase());
                const context = {
                    source: "browse",
                    sourceKey: this.getSelectedPackageSource(),
                    latestVersion: cached?.version,
                    installedVersion: this.getInstalledPackage(packageId)?.version || null
                };
                this.showPackageDetails(packageId, context);
            });
        });
    }
    renderInstalled() {
        if (!this.installedListEl) {
            return;
        }

        const packages = this.getInstalledPackages();
        if (packages.length === 0) {
            this.installedListEl.innerHTML = '<div class="nuget-empty">暂无安装的包</div>';
            return;
        }

        const rows = packages.map((pkg) => {
            const updateInfo = this.pendingUpdates.find((item) => item.id.toLowerCase() === pkg.id.toLowerCase());
            const badge = updateInfo ? '<span class="nuget-status-label update">可更新</span>' : "";
            return `
                <div class="nuget-result-item" data-source="installed" data-package-id="${escapeHtml(pkg.id)}">
                    <div class="nuget-result-header">
                        <div class="nuget-package-id">${escapeHtml(pkg.id)}</div>
                        <div class="nuget-package-version">当前 ${escapeHtml(pkg.version)}</div>
                    </div>
                    <div class="nuget-meta">
                        ${badge}
                    </div>
                </div>
            `;
        }).join("");

        this.installedListEl.innerHTML = rows;
        this.installedListEl.querySelectorAll(".nuget-result-item").forEach((element) => {
            element.addEventListener("click", () => {
                const packageId = element.dataset.packageId;
                const pkg = this.getInstalledPackage(packageId);
                const updateInfo = this.pendingUpdates.find((item) => item.id.toLowerCase() === packageId.toLowerCase());
                const context = {
                    source: "installed",
                    sourceKey: this.getSelectedPackageSource(),
                    installedVersion: pkg?.version || null,
                    latestVersion: updateInfo?.latestVersion || pkg?.version || null
                };
                this.showPackageDetails(packageId, context);
            });
        });
    }

    renderUpdatesList() {
        if (!this.updatesListEl) {
            return;
        }

        if (!this.pendingUpdates.length) {
            this.updatesListEl.innerHTML = '<div class="nuget-empty">没有检测到可更新的包</div>';
            return;
        }

        const items = this.pendingUpdates.map((item) => `
            <div class="nuget-result-item" data-source="updates" data-package-id="${escapeHtml(item.id)}">
                <div class="nuget-result-header">
                    <div class="nuget-package-id">${escapeHtml(item.id)}</div>
                    <div class="nuget-package-version">${escapeHtml(item.installedVersion)} → ${escapeHtml(item.latestVersion)}</div>
                </div>
                <div class="nuget-meta">
                    <span class="nuget-status-label update">可更新</span>
                </div>
            </div>
        `).join("");

        this.updatesListEl.innerHTML = items;
        this.updatesListEl.querySelectorAll(".nuget-result-item").forEach((element) => {
            element.addEventListener("click", () => {
                const packageId = element.dataset.packageId;
                const updateInfo = this.pendingUpdates.find((pkg) => pkg.id.toLowerCase() === packageId.toLowerCase());
                const pkg = this.getInstalledPackage(packageId);
                const context = {
                    source: "updates",
                    sourceKey: this.getSelectedPackageSource(),
                    installedVersion: pkg?.version || null,
                    latestVersion: updateInfo?.latestVersion || null
                };
                this.showPackageDetails(packageId, context);
            });
        });
    }
    async refreshUpdates({ background = false } = {}) {
        if (!this.currentFile) {
            return;
        }

        const packages = this.getInstalledPackages();
        if (!packages.length) {
            this.pendingUpdates = [];
            if (!background && this.updatesListEl) {
                this.updatesListEl.innerHTML = '<div class="nuget-empty">没有检测到可更新的包</div>';
            }
            return;
        }

        const sourceKey = this.getSelectedPackageSource();

        if (!background && this.updatesListEl) {
            this.updatesListEl.innerHTML = '<div class="nuget-loading">正在检查更新...</div>';
        }

        try {
            const updates = await Promise.all(packages.map(async (pkg) => {
                try {
                    const latest = await this.fetchLatestVersion(pkg.id, sourceKey);
                    const hasUpdate = compareVersions(latest, pkg.version) > 0;
                    return {
                        id: pkg.id,
                        installedVersion: pkg.version,
                        latestVersion: latest,
                        updateAvailable: hasUpdate
                    };
                } catch (error) {
                    console.warn(`获取 ${pkg.id} 最新版本失败`, error);
                    return {
                        id: pkg.id,
                        installedVersion: pkg.version,
                        latestVersion: pkg.version,
                        updateAvailable: false
                    };
                }
            }));

            this.pendingUpdates = updates.filter((item) => item.updateAvailable);
            this.renderInstalled();
            if (!background || this.activeTab === "updates") {
                this.renderUpdatesList();
            }
        } catch (error) {
            console.error("刷新 NuGet 更新失败", error);
            if (!background && this.updatesListEl) {
                this.updatesListEl.innerHTML = `<div class="nuget-error">检查更新失败：${escapeHtml(error.message || "未知错误")}</div>`;
            }
        }
    }
    async showPackageDetails(packageId, context = {}) {
        if (!packageId) {
            return;
        }

        this.highlightSelection(packageId, context.source);
        const sourceKey = context.sourceKey || this.getSelectedPackageSource();
        const requestToken = ++this.detailRequestToken;
        if (this.detailsPanel) {
            this.detailsPanel.innerHTML = '<div class="nuget-loading">正在加载包信息...</div>';
        }

        try {
            const metadata = await this.fetchPackageMetadata(packageId, sourceKey);
            if (this.detailRequestToken !== requestToken) {
                return;
            }
            const versions = await this.fetchPackageVersions(packageId, sourceKey);
            if (this.detailRequestToken !== requestToken) {
                return;
            }

            const includePrerelease = Boolean(this.includePrerelease?.checked);
            const installed = this.getInstalledPackage(packageId);
            const installedVersion = installed?.version || context.installedVersion || null;
            const baseLatestVersion = context.latestVersion || metadata.version || (versions.at(-1) || null);

            const filteredVersions = includePrerelease
                ? [...versions]
                : versions.filter((version) => !isPrereleaseVersion(version));

            const uniqueVersions = Array.from(new Set(filteredVersions));
            uniqueVersions.sort((a, b) => compareVersions(a, b));

            const reversedVersions = uniqueVersions.slice().reverse();
            const hasVersions = reversedVersions.length > 0;

            const latestVersion = includePrerelease
                ? baseLatestVersion
                : (uniqueVersions.at(-1) || (installedVersion && !isPrereleaseVersion(installedVersion) ? installedVersion : null) || baseLatestVersion);

            const selectedVersion = hasVersions
                ? reversedVersions.find((version) => version === latestVersion)
                    || (installedVersion && reversedVersions.find((version) => version === installedVersion))
                    || reversedVersions[0]
                : (installedVersion || metadata.version || '');

            const versionOptions = hasVersions
                ? reversedVersions
                    .map((version) => `<option value="${escapeHtml(version)}" ${version === selectedVersion ? "selected" : ""}>${escapeHtml(version)}</option>`)
                    .join("")
                : '<option value="" disabled>暂无可用版本</option>';

            const links = [];
            if (metadata.projectUrl) {
                links.push(buildLink(metadata.projectUrl, "项目主页"));
            }
            if (metadata.licenseUrl) {
                links.push(buildLink(metadata.licenseUrl, "许可证"));
            }
            if (metadata.iconUrl) {
                links.push(buildLink(metadata.iconUrl, "图标"));
            }

            const description = metadata.description || metadata.summary || "暂无描述";
            const isInstalled = Boolean(installed);
            const versionDiff = isInstalled && selectedVersion ? compareVersions(selectedVersion, installedVersion) : 0;
            let primaryLabel = "安装";
            if (isInstalled) {
                if (versionDiff > 0) {
                    primaryLabel = "更新";
                } else if (versionDiff < 0) {
                    primaryLabel = "降级";
                } else {
                    primaryLabel = "重新安装";
                }
            }

            this.detailsPanel.innerHTML = `
                <div class="nuget-details-header">
                    <div class="nuget-details-title">${escapeHtml(metadata.id)}</div>
                    <div class="nuget-details-version">选定版本：<span id="nugetSelectedVersionLabel">${escapeHtml(selectedVersion)}</span></div>
                </div>
                <div class="nuget-details-desc">${escapeHtml(description)}</div>
                <div class="nuget-details-field">
                    <span>作者</span>
                    <span>${escapeHtml(metadata.authors || "未知")}</span>
                </div>
                <div class="nuget-details-field">
                    <span>总下载量</span>
                    <span>${formatDownloads(metadata.totalDownloads || 0)}</span>
                </div>
                <div class="nuget-details-field">
                    <span>版本</span>
                    <select class="nuget-version-select" id="nugetVersionSelect">${versionOptions}</select>
                </div>
                ${links.length ? `<div class="nuget-links">${links.join("")}</div>` : ""}
                <div class="nuget-details-actions">
                    <button class="nuget-primary-btn" id="nugetPrimaryAction">${primaryLabel}</button>
                    ${isInstalled ? '<button class="nuget-secondary-btn danger" id="nugetUninstallAction">卸载</button>' : ""}
                </div>
            `;

            const versionSelect = document.getElementById("nugetVersionSelect");
            const versionLabel = document.getElementById("nugetSelectedVersionLabel");
            const primaryButton = document.getElementById("nugetPrimaryAction");
            const uninstallButton = document.getElementById("nugetUninstallAction");

            if (versionSelect && versionLabel && primaryButton) {
                const updatePrimaryButtonState = () => {
                    const value = versionSelect.value;
                    versionLabel.textContent = value || '无可用版本';
                    if (!value) {
                        primaryButton.textContent = '无可用版本';
                        primaryButton.disabled = true;
                        return;
                    }

                    primaryButton.disabled = false;
                    if (isInstalled) {
                        const diff = compareVersions(value, installedVersion);
                        if (diff > 0) {
                            primaryButton.textContent = '更新';
                        } else if (diff < 0) {
                            primaryButton.textContent = '降级';
                        } else {
                            primaryButton.textContent = '重新安装';
                        }
                    } else {
                        primaryButton.textContent = '安装';
                    }
                };

                updatePrimaryButtonState();

                versionSelect.addEventListener("change", () => {
                    updatePrimaryButtonState();
                });

                primaryButton.addEventListener("click", async () => {
                    const targetVersion = versionSelect.value;
                    if (!targetVersion) {
                        return;
                    }
                    await this.installOrUpdatePackage(metadata.id, targetVersion, installedVersion);
                });
            }

            if (uninstallButton) {
                uninstallButton.addEventListener("click", async () => {
                    await this.uninstallPackage(metadata.id);
                });
            }
        } catch (error) {
            console.error("加载包详情失败", error);
            if (this.detailsPanel) {
                this.detailsPanel.innerHTML = `<div class="nuget-error">加载包详情失败：${escapeHtml(error.message || "未知错误")}</div>`;
            }
        }
    }
    highlightSelection(packageId, source) {
        document.querySelectorAll(".nuget-result-item.selected").forEach((element) => {
            element.classList.remove("selected");
        });
        if (!packageId) {
            return;
        }
        const selector = `.nuget-result-item[data-package-id="${cssEscape(packageId)}"]${source ? `[data-source="${cssEscape(source)}"]` : ""}`;
        const target = document.querySelector(selector);
        if (target) {
            target.classList.add("selected");
            target.scrollIntoView({ block: "nearest" });
        }
    }

    clearDetailsPanel() {
        if (this.detailsPanel) {
            this.detailsPanel.innerHTML = '<div class="nuget-details-placeholder">选择一个包查看详细信息</div>';
        }
    }

    getInstalledPackages() {
        if (!this.currentFile?.nugetConfig?.packages) {
            return [];
        }
        return this.currentFile.nugetConfig.packages;
    }

    getInstalledPackage(packageId) {
        const packages = this.getInstalledPackages();
        return packages.find((pkg) => pkg.id.toLowerCase() === packageId.toLowerCase()) || null;
    }

    mergePackagesFromServer(packageEntries) {
        if (!this.currentFile) {
            return [];
        }

        if (!Array.isArray(packageEntries) || packageEntries.length === 0) {
            return this.getInstalledPackages();
        }

        if (!this.currentFile.nugetConfig) {
            this.currentFile.nugetConfig = { packages: [] };
        }

        if (!Array.isArray(this.currentFile.nugetConfig.packages)) {
            this.currentFile.nugetConfig.packages = [];
        }

        const installed = this.currentFile.nugetConfig.packages;

        for (const entry of packageEntries) {
            if (!entry) {
                continue;
            }

            const idValue = entry.id ?? entry.Id;
            if (!idValue) {
                continue;
            }

            const normalizedId = String(idValue).trim();
            if (!normalizedId) {
                continue;
            }

            const versionValue = entry.version ?? entry.Version;
            const normalizedVersion = versionValue ? String(versionValue).trim() : "";

            const existingIndex = installed.findIndex((pkg) => pkg.id.toLowerCase() === normalizedId.toLowerCase());
            if (existingIndex >= 0) {
                installed[existingIndex].version = normalizedVersion;
            } else {
                installed.push({ id: normalizedId, version: normalizedVersion });
            }
        }

        return installed;
    }
    async fetchPackageMetadata(packageId, sourceKey = this.getSelectedPackageSource()) {
        const cacheKey = this.composeSourceCacheKey(sourceKey, packageId);
        if (this.packageCache.has(cacheKey)) {
            return this.packageCache.get(cacheKey);
        }

        const activeSource = this.getActiveSource(sourceKey);
        const searchEndpoint = activeSource?.searchUrl || DEFAULT_PACKAGE_SOURCES[0].searchUrl;
        if (!searchEndpoint) {
            throw new Error('当前包源缺少搜索地址');
        }

        let url;
        try {
            url = new URL(searchEndpoint);
        } catch (error) {
            throw new Error('当前包源的搜索地址无效');
        }

        url.searchParams.set('q', `PackageId:${packageId}`);
        url.searchParams.set('take', '1');
        url.searchParams.set('prerelease', 'true');

        const response = await fetch(url.toString());
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }
        const payload = await response.json();
        const metadata = payload.data && payload.data[0];
        if (!metadata) {
            throw new Error('未找到包元数据');
        }
        this.packageCache.set(cacheKey, metadata);
        return metadata;
    }

    async fetchPackageVersions(packageId, sourceKey = this.getSelectedPackageSource()) {
        const cacheKey = this.composeSourceCacheKey(sourceKey, packageId);
        const cached = this.latestVersionCache.get(cacheKey);
        if (cached?.versions) {
            return cached.versions;
        }

        // Always use proxy to avoid CORS issues
        const versions = await this.fetchPackageVersionsViaProxy(packageId, sourceKey);

        this.latestVersionCache.set(cacheKey, {
            latestVersion: versions.at(-1) || null,
            versions
        });
        return versions;
    }

    async fetchLatestVersion(packageId, sourceKey = this.getSelectedPackageSource()) {
        const cacheKey = this.composeSourceCacheKey(sourceKey, packageId);
        const cached = this.latestVersionCache.get(cacheKey);
        if (cached?.latestVersion) {
            return cached.latestVersion;
        }
        const versions = await this.fetchPackageVersions(packageId, sourceKey);
        const latest = versions.at(-1) || '0.0.0';
        this.latestVersionCache.set(cacheKey, {
            latestVersion: latest,
            versions
        });
        return latest;
    }

    async fetchPackageVersionsViaProxy(packageId, sourceKey = this.getSelectedPackageSource()) {
        const params = new URLSearchParams({ packageId });
        if (sourceKey) {
            params.set("sourceKey", sourceKey);
        }

        const response = await fetch(`/api/NugetProxy/versions?${params.toString()}`, {
            credentials: "include"
        });

        if (!response.ok) {
            throw new Error(`代理请求失败 (HTTP ${response.status})`);
        }

        const payload = await response.json();
        if (payload?.code !== 0) {
            throw new Error(payload?.message || "代理请求失败");
        }

        const versions = Array.isArray(payload?.data) ? payload.data : [];
        if (versions.length === 0) {
            console.warn("NuGet proxy returned empty version list for", packageId, sourceKey);
        }
        return versions;
    }

    async installOrUpdatePackage(packageId, targetVersion, installedVersion) {
        if (!this.currentFile) {
            return;
        }

        const normalizedPackageId = (packageId || "").trim();
        if (!normalizedPackageId) {
            return;
        }

        if (!targetVersion) {
            this.notify("请选择要安装的版本", "error");
            return;
        }

        const packages = this.getInstalledPackages();
        const existingIndex = packages.findIndex((pkg) => pkg.id.toLowerCase() === normalizedPackageId.toLowerCase());

        if (existingIndex >= 0 && packages[existingIndex].version === targetVersion) {
            this.notify(`包 ${normalizedPackageId} 已是版本 ${targetVersion}`, "info");
            return;
        }

        let finalVersion = targetVersion;

        try {
            const request = {
                Packages: [{ Id: normalizedPackageId, Version: targetVersion }],
                SourceKey: this.getSelectedPackageSource()
            };
            const result = await this.sendRequest("addPackages", request);
            if (!result?.data || result.data.code !== 0) {
                throw new Error(result?.data?.message || "安装失败");
            }

            const serverPackages = result?.data?.data?.packages;
            if (Array.isArray(serverPackages) && serverPackages.length > 0) {
                const updatedPackages = this.mergePackagesFromServer(serverPackages);
                const currentEntry = updatedPackages.find(
                    (pkg) => pkg.id.toLowerCase() === normalizedPackageId.toLowerCase()
                );
                finalVersion = currentEntry?.version || finalVersion;
            } else {
                const versionToUse = targetVersion || installedVersion || "";
                if (existingIndex >= 0) {
                    packages[existingIndex].version = versionToUse;
                } else {
                    packages.push({ id: normalizedPackageId, version: versionToUse });
                }
                finalVersion = versionToUse || finalVersion;
            }
        } catch (error) {
            console.error("安装包失败", error);
            this.notify(`安装 ${normalizedPackageId} 失败：${error.message || "未知错误"}`, "error");
            return;
        }

        this.persistFile();
        this.renderInstalled();
        this.refreshUpdates({ background: true });
        this.renderBrowseResults(this.lastSearchResults);

        const versionForDiff = finalVersion || targetVersion || installedVersion;
        const displayVersion = versionForDiff || "最新版本";
        if (installedVersion) {
            const diff = compareVersions(versionForDiff || installedVersion, installedVersion);
            if (diff > 0) {
                this.notify(`已更新 ${normalizedPackageId} 到 ${displayVersion}`, "success");
            } else if (diff < 0) {
                this.notify(`已降级 ${normalizedPackageId} 到 ${displayVersion}`, "success");
            } else {
                this.notify(`已重新安装 ${normalizedPackageId} (${displayVersion})`, "success");
            }
        } else {
            this.notify(`已安装 ${normalizedPackageId} (${displayVersion})`, "success");
        }
    }

    async uninstallPackage(packageId) {
        if (!this.currentFile) {
            return;
        }
        const packages = this.getInstalledPackages();
        const index = packages.findIndex((pkg) => pkg.id.toLowerCase() === packageId.toLowerCase());
        if (index === -1) {
            this.notify(`未找到包 ${packageId}`, "error");
            return;
        }

        const packageToRemove = packages[index];

        // Call backend to physically delete package files
        try {
            const request = {
                Packages: [{ Id: packageToRemove.id, Version: packageToRemove.version }]
            };
            const result = await this.sendRequest("removePackages", request);
            if (!result?.data || result.data.code !== 0) {
                console.warn("Failed to delete package files:", result?.data?.message || "Unknown error");
                // Continue with uninstall even if file deletion fails
            }
        } catch (error) {
            console.error("Error deleting package files:", error);
            // Continue with uninstall even if file deletion fails
        }

        packages.splice(index, 1);
        this.persistFile();
        this.renderInstalled();
        this.refreshUpdates({ background: true });
        this.renderBrowseResults(this.lastSearchResults);
        this.clearDetailsPanel();
        this.notify(`已卸载 ${packageId}`, "success");
    }

    persistFile() {
        if (!this.currentFile) {
            return;
        }
        const files = window.GetCurrentFiles();
        const update = (items) => {
            for (const item of items) {
                if (item.id === this.currentFile.id) {
                    item.nugetConfig = this.currentFile.nugetConfig;
                    return true;
                }
                if (item.type === "folder" && Array.isArray(item.files)) {
                    const found = update(item.files);
                    if (found) {
                        return true;
                    }
                }
            }
            return false;
        };
        update(files);
        localStorage.setItem("controllerFiles", JSON.stringify(files));
    }
}

