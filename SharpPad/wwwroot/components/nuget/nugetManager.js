const NUGET_SEARCH_ENDPOINT = "https://azuresearch-usnc.nuget.org/query";
const NUGET_FLAT_CONTAINER = "https://api.nuget.org/v3-flatcontainer";
const SEARCH_DEBOUNCE_MS = 350;

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
    initialize() {
        if (!this.dialog) {
            return;
        }

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
                if (this.activeTab === "browse") {
                    this.performSearch(true);
                }
            });
        }

        if (this.searchButton) {
            this.searchButton.addEventListener("click", () => {
                this.performSearch(true);
            });
        }

        this.dialog.addEventListener("click", (event) => {
            if (event.target === this.dialog) {
                this.close();
            }
        });

        document.addEventListener("keydown", (event) => {
            if (event.key === "Escape" && this.dialog.style.display === "block") {
                this.close();
            }
        });
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
        return this.packageSourceSelect?.value || "nuget";
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
        const url = new URL(NUGET_SEARCH_ENDPOINT);
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

        if (!background && this.updatesListEl) {
            this.updatesListEl.innerHTML = '<div class="nuget-loading">正在检查更新...</div>';
        }

        try {
            const updates = await Promise.all(packages.map(async (pkg) => {
                try {
                    const latest = await this.fetchLatestVersion(pkg.id);
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
        const requestToken = ++this.detailRequestToken;
        if (this.detailsPanel) {
            this.detailsPanel.innerHTML = '<div class="nuget-loading">正在加载包信息...</div>';
        }

        try {
            const metadata = await this.fetchPackageMetadata(packageId);
            if (this.detailRequestToken !== requestToken) {
                return;
            }
            const versions = await this.fetchPackageVersions(packageId);
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
    async fetchPackageMetadata(packageId) {
        const cacheKey = packageId.toLowerCase();
        if (this.packageCache.has(cacheKey)) {
            return this.packageCache.get(cacheKey);
        }

        const url = new URL(NUGET_SEARCH_ENDPOINT);
        url.searchParams.set("q", `PackageId:${packageId}`);
        url.searchParams.set("take", "1");
        url.searchParams.set("prerelease", "true");

        const response = await fetch(url.toString());
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }
        const payload = await response.json();
        const metadata = payload.data && payload.data[0];
        if (!metadata) {
            throw new Error("未找到包元数据");
        }
        this.packageCache.set(cacheKey, metadata);
        return metadata;
    }

    async fetchPackageVersions(packageId) {
        const cacheKey = packageId.toLowerCase();
        const cached = this.latestVersionCache.get(cacheKey);
        if (cached?.versions) {
            return cached.versions;
        }
        const response = await fetch(`${NUGET_FLAT_CONTAINER}/${packageId.toLowerCase()}/index.json`);
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }
        const payload = await response.json();
        const versions = Array.isArray(payload.versions) ? payload.versions : [];
        this.latestVersionCache.set(cacheKey, {
            latestVersion: versions.at(-1) || null,
            versions
        });
        return versions;
    }

    async fetchLatestVersion(packageId) {
        const cacheKey = packageId.toLowerCase();
        const cached = this.latestVersionCache.get(cacheKey);
        if (cached?.latestVersion) {
            return cached.latestVersion;
        }
        const versions = await this.fetchPackageVersions(packageId);
        const latest = versions.at(-1) || "0.0.0";
        this.latestVersionCache.set(cacheKey, {
            latestVersion: latest,
            versions
        });
        return latest;
    }
    async installOrUpdatePackage(packageId, targetVersion, installedVersion) {
        if (!this.currentFile) {
            return;
        }

        if (!targetVersion) {
            this.notify("请选择要安装的版本", "error");
            return;
        }

        const packages = this.getInstalledPackages();
        const existingIndex = packages.findIndex((pkg) => pkg.id.toLowerCase() === packageId.toLowerCase());

        if (existingIndex >= 0 && packages[existingIndex].version === targetVersion) {
            this.notify(`包 ${packageId} 已是版本 ${targetVersion}`, "info");
            return;
        }

        try {
            const request = {
                Packages: [{ Id: packageId, Version: targetVersion }],
                SourceKey: this.getSelectedPackageSource()
            };
            const result = await this.sendRequest("addPackages", request);
            if (!result?.data || result.data.code !== 0) {
                throw new Error(result?.data?.message || "安装失败");
            }
        } catch (error) {
            console.error("安装包失败", error);
            this.notify(`安装 ${packageId} 失败：${error.message || "未知错误"}`, "error");
            return;
        }

        if (existingIndex >= 0) {
            packages[existingIndex].version = targetVersion;
        } else {
            packages.push({ id: packageId, version: targetVersion });
        }

        this.persistFile();
        this.renderInstalled();
        this.refreshUpdates({ background: true });
        this.renderBrowseResults(this.lastSearchResults);

        if (installedVersion) {
            const diff = compareVersions(targetVersion, installedVersion);
            if (diff > 0) {
                this.notify(`已更新 ${packageId} 到 ${targetVersion}`, "success");
            } else if (diff < 0) {
                this.notify(`已降级 ${packageId} 到 ${targetVersion}`, "success");
            } else {
                this.notify(`已重新安装 ${packageId} (${targetVersion})`, "success");
            }
        } else {
            this.notify(`已安装 ${packageId} (${targetVersion})`, "success");
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



