// Multi-file directive utilities

export function extractDirectiveReferences(code) {
    if (typeof code !== 'string') {
        return [];
    }

    const references = new Set();
    const directiveRegex = /^\s*\/\/\s*@\s*(.+)$/gm;
    let match;

    while ((match = directiveRegex.exec(code)) !== null) {
        const cleaned = match[1]
            .replace(/\s*\/\/.*$/, '')
            .trim();

        if (!cleaned) {
            continue;
        }

        cleaned
            .split(/[;,\s]+/)
            .map(token => token.trim())
            .filter(Boolean)
            .map(token => token.replace(/^["']|["']$/g, ''))
            .filter(Boolean)
            .forEach(token => references.add(token));
    }

    return Array.from(references);
}

export async function buildMultiFileContext({ entryFileId = null, entryFileName = null, entryContent = '', entryPackages = [] } = {}) {
    // 从后端 API 获取文件树和文件内容
    const { FileManager } = await import('../fileSystem/fileManager.js');
    const fileManager = FileManager.getInstance();

    const filesById = new Map();
    const filesByName = new Map();
    const filesByPath = new Map();
    const packageMap = new Map();

    function sanitizePackages(source) {
        if (!Array.isArray(source)) {
            return [];
        }

        const cleaned = [];
        for (const pkg of source) {
            if (!pkg || typeof pkg !== 'object') {
                continue;
            }

            const id = typeof pkg.id === 'string'
                ? pkg.id.trim()
                : typeof pkg.Id === 'string'
                    ? pkg.Id.trim()
                    : '';

            if (!id) {
                continue;
            }

            const version = typeof pkg.version === 'string'
                ? pkg.version.trim()
                : typeof pkg.Version === 'string'
                    ? pkg.Version.trim()
                    : '';

            cleaned.push({ id, version });
        }

        return cleaned;
    }

    function mergePackages(targetMap, packages) {
        if (!Array.isArray(packages)) {
            return;
        }

        for (const pkg of packages) {
            if (!pkg || typeof pkg.id !== 'string') {
                continue;
            }

            const key = pkg.id.toLowerCase();
            if (!targetMap.has(key)) {
                targetMap.set(key, { id: pkg.id, version: pkg.version || '' });
                continue;
            }

            const existing = targetMap.get(key);
            if (!existing.version && pkg.version) {
                targetMap.set(key, { id: pkg.id, version: pkg.version });
            }
        }
    }

    // 从文件树递归索引所有文件
    async function indexFiles(items, parentSegments = []) {
        if (!Array.isArray(items)) {
            return;
        }

        for (const item of items) {
            if (!item) continue;

            if (item.type === 'folder') {
                if (item.children) {
                    await indexFiles(item.children, parentSegments.concat(item.name || ''));
                }
                continue;
            }

            const path = item.path;
            const name = item.name;

            // 读取文件内容
            let content = '';
            try {
                const resp = await fetch(`/api/fs/file?path=${encodeURIComponent(path)}`);
                if (resp.ok) {
                    const data = await resp.json();
                    content = data.content || '';
                }
            } catch {
                // Skip files we can't read
            }

            // 尝试读取元数据
            let packages = [];
            try {
                const dir = path.includes('/') ? path.substring(0, path.lastIndexOf('/')) : '';
                const metaPath = dir ? `${dir}/.sharppad/${name}.json` : `.sharppad/${name}.json`;
                const metaResp = await fetch(`/api/fs/file?path=${encodeURIComponent(metaPath)}`);
                if (metaResp.ok) {
                    const metaData = await metaResp.json();
                    const meta = JSON.parse(metaData.content || '{}');
                    packages = sanitizePackages(meta.nugetPackages);
                }
            } catch {
                // No metadata
            }

            const record = {
                id: path,
                name,
                path,
                content,
                packages
            };

            filesById.set(path, record);

            const nameKey = (name || '').toLowerCase();
            if (!filesByName.has(nameKey)) {
                filesByName.set(nameKey, []);
            }
            filesByName.get(nameKey).push(record);

            const pathKey = path.toLowerCase();
            if (pathKey) {
                filesByPath.set(pathKey, record);
            }
        }
    }

    // 获取文件树并索引
    try {
        const treeResp = await fetch('/api/fs/tree');
        if (treeResp.ok) {
            const tree = await treeResp.json();
            await indexFiles(tree);
        }
    } catch {
        // Failed to get tree
    }

    function normalizeReference(reference) {
        if (typeof reference !== 'string') {
            return '';
        }

        return reference
            .replace(/^["'\s]+/, '')
            .replace(/["'\s]+$/, '')
            .replace(/^[.\/\\]+/, '')
            .replace(/\\/g, '/')
            .trim();
    }

    function resolveReference(reference) {
        const normalized = normalizeReference(reference);
        if (!normalized) {
            return null;
        }

        const lower = normalized.toLowerCase();

        if (filesByPath.has(lower)) {
            return filesByPath.get(lower);
        }

        const segments = lower.split('/').filter(Boolean);
        const fileNameKey = segments.length > 0 ? segments[segments.length - 1] : lower;
        const candidates = filesByName.get(fileNameKey);

        if (!candidates || candidates.length === 0) {
            return null;
        }

        if (candidates.length === 1) {
            return candidates[0];
        }

        const exactPathMatch = candidates.find(candidate => candidate.path && candidate.path.toLowerCase() === lower);
        if (exactPathMatch) {
            return exactPathMatch;
        }

        const suffixMatch = candidates.find(candidate => candidate.path && candidate.path.toLowerCase().endsWith(lower));
        if (suffixMatch) {
            return suffixMatch;
        }

        return candidates[0];
    }

    function createEntryRecord() {
        if (entryFileId && filesById.has(entryFileId)) {
            return filesById.get(entryFileId);
        }

        if (entryFileName) {
            const key = entryFileName.toLowerCase();
            const candidates = filesByName.get(key);
            if (candidates && candidates.length > 0) {
                return candidates[0];
            }
        }

        if (entryFileName) {
            return {
                id: entryFileId,
                name: entryFileName,
                path: entryFileName,
                content: entryContent || '',
                packages: sanitizePackages(entryPackages)
            };
        }

        return null;
    }

    const entryRecord = createEntryRecord();
    if (!entryRecord) {
        return {
            files: [],
            autoIncludedNames: [],
            missingReferences: [],
            packages: []
        };
    }

    if (Array.isArray(entryPackages) && entryPackages.length > 0) {
        const sanitizedEntry = sanitizePackages(entryPackages);
        if (sanitizedEntry.length > 0) {
            const existing = Array.isArray(entryRecord.packages) ? entryRecord.packages : [];
            const existingMap = new Map(existing.map(pkg => [pkg.id.toLowerCase(), pkg]));
            sanitizedEntry.forEach(pkg => {
                const key = pkg.id.toLowerCase();
                if (!existingMap.has(key)) {
                    existing.push(pkg);
                    existingMap.set(key, pkg);
                } else if (!existingMap.get(key).version && pkg.version) {
                    existingMap.set(key, { id: pkg.id, version: pkg.version });
                }
            });
            entryRecord.packages = Array.from(existingMap.values());
        }
    }

    const normalizedEntryContent = typeof entryContent === 'string' ? entryContent : entryRecord.content || '';

    const files = [];
    const autoIncludedNames = new Set();
    const missingReferences = new Set();
    const visitedKeys = new Set();
    const queue = [];

    function createVisitedKey(record) {
        return record.id || record.path || record.name;
    }

    function enqueue(record, content, isEntry = false) {
        const key = createVisitedKey(record);
        if (!key || visitedKeys.has(key)) {
            return false;
        }

        visitedKeys.add(key);
        const filePackages = Array.isArray(record.packages) ? record.packages : [];
        files.push({
            id: record.id || null,
            name: record.name || record.path || 'Program.cs',
            content: typeof content === 'string' ? content : '',
            isEntry,
            packages: filePackages
        });

        queue.push({ record, content });
        if (!isEntry) {
            autoIncludedNames.add(record.name || record.path || '未知文件');
        }

        mergePackages(packageMap, filePackages);
        return true;
    }

    enqueue(entryRecord, normalizedEntryContent, true);

    while (queue.length > 0) {
        const current = queue.shift();
        const currentContent = typeof current.content === 'string' ? current.content : '';
        const references = extractDirectiveReferences(currentContent);

        references.forEach(reference => {
            const resolved = resolveReference(reference);
            if (resolved) {
                enqueue(resolved, resolved.content, false);
            } else {
                missingReferences.add(reference);
            }
        });
    }

    return {
        files,
        autoIncludedNames: Array.from(autoIncludedNames),
        missingReferences: Array.from(missingReferences),
        packages: Array.from(packageMap.values())
    };
}
