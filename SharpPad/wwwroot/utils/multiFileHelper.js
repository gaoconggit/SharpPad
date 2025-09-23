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

export function buildMultiFileContext({ entryFileId = null, entryFileName = null, entryContent = '' } = {}) {
    const filesCacheRaw = localStorage.getItem('controllerFiles');
    const filesCache = filesCacheRaw ? JSON.parse(filesCacheRaw) : [];

    const filesById = new Map();
    const filesByName = new Map();
    const filesByPath = new Map();

    function indexFiles(items, parentSegments = []) {
        if (!Array.isArray(items)) {
            return;
        }

        items.forEach(item => {
            if (!item) {
                return;
            }

            if (item.type === 'folder') {
                indexFiles(item.files, parentSegments.concat(item.name || ''));
                return;
            }

            const segments = parentSegments.concat(item.name || '');
            const path = segments.filter(Boolean).join('/');
            const record = {
                id: item.id,
                name: item.name,
                path,
                content: localStorage.getItem(`file_${item.id}`) ?? item.content ?? ''
            };

            filesById.set(item.id, record);

            const nameKey = (item.name || '').toLowerCase();
            if (!filesByName.has(nameKey)) {
                filesByName.set(nameKey, []);
            }
            filesByName.get(nameKey).push(record);

            const pathKey = path.toLowerCase();
            if (pathKey) {
                filesByPath.set(pathKey, record);
            }
        });
    }

    indexFiles(filesCache);

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
                content: entryContent || ''
            };
        }

        return null;
    }

    const entryRecord = createEntryRecord();
    if (!entryRecord) {
        return {
            files: [],
            autoIncludedNames: [],
            missingReferences: []
        };
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
        files.push({
            id: record.id || null,
            name: record.name || record.path || 'Program.cs',
            content: typeof content === 'string' ? content : '',
            isEntry
        });

        queue.push({ record, content });
        if (!isEntry) {
            autoIncludedNames.add(record.name || record.path || '未知文件');
        }

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
        missingReferences: Array.from(missingReferences)
    };
}
