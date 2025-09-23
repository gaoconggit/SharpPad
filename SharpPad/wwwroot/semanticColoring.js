import { getCurrentFile, shouldUseMultiFileMode, createMultiFileRequest } from './utils/common.js';
import { sendRequest } from './utils/apiService.js';

/**
 * Visual Studio 风格的语义着色功能
 * 为 C# 代码提供基于 Roslyn 的语义高亮
 */
export function setupSemanticColoring() {
    console.log('Setting up semantic coloring...');

    // 定义语义令牌类型映射（与后端对应）
    const tokenLegend = {
        tokenTypes: [
            'namespace', 'class', 'enum', 'interface', 'struct', 'typeParameter',
            'parameter', 'variable', 'property', 'enumMember', 'event',
            'function', 'method', 'macro', 'keyword', 'modifier',
            'comment', 'string', 'number', 'regexp', 'operator'
        ],
        tokenModifiers: [
            'declaration', 'definition', 'readonly', 'static', 'deprecated',
            'abstract', 'async', 'modification', 'documentation', 'defaultLibrary'
        ]
    };

    // 添加 Visual Studio 风格的 CSS 样式
    addVSStyles();

    // 注册语义令牌 Provider
    registerSemanticTokensProvider(tokenLegend);

    // 设置自动语义着色
    setupAutoSemanticColoring(tokenLegend);
}

/**
 * 注册 Monaco Editor 的语义令牌 Provider
 */
function registerSemanticTokensProvider(legend) {
    try {
        if (typeof monaco.languages.registerDocumentSemanticTokensProvider === 'function') {
            monaco.languages.registerDocumentSemanticTokensProvider('csharp', {
                getLegend() {
                    return legend;
                },

                async provideDocumentSemanticTokens(model) {
                    return await getSemanticTokens(model);
                },

                releaseDocumentSemanticTokens(resultId) {
                    // 释放资源
                }
            });
            console.log('Semantic tokens provider registered');
        } else {
            console.warn('DocumentSemanticTokensProvider not supported');
        }
    } catch (error) {
        console.error('Failed to register semantic tokens provider:', error);
    }
}

// 语义令牌缓存
const tokenCache = new Map();

// Web Worker 支持（如果可用）
let semanticWorker = null;
const ENABLE_WORKER = typeof Worker !== 'undefined' && window.location.protocol !== 'file:';

if (ENABLE_WORKER) {
    try {
        // 创建内联 Worker
        const workerCode = `
            self.onmessage = function(e) {
                const { action, data } = e.data;
                
                if (action === 'processTokens') {
                    const { tokenData, commentLines, stringRanges } = data;
                    const result = processTokensInWorker(tokenData, commentLines, stringRanges);
                    self.postMessage({ action: 'tokenProcessed', result });
                }
            };
            
            function processTokensInWorker(tokenData, commentLines, stringRanges) {
                const decorations = [];
                let currentLine = 0;
                let currentChar = 0;
                
                for (let i = 0; i < tokenData.length; i += 5) {
                    const deltaLine = tokenData[i];
                    const deltaChar = tokenData[i + 1];
                    const length = tokenData[i + 2];
                    const typeIndex = tokenData[i + 3];
                    const modifiers = tokenData[i + 4];
                    
                    currentLine += deltaLine;
                    currentChar = deltaLine === 0 ? currentChar + deltaChar : deltaChar;
                    
                    const isCommentLine = commentLines.has(currentLine + 1);
                    const isInString = isPositionInStringWorker({ line: currentLine + 1, char: currentChar }, stringRanges);
                    
                    decorations.push({
                        line: currentLine + 1,
                        char: currentChar + 1,
                        length: length,
                        typeIndex: typeIndex,
                        modifiers: modifiers,
                        isComment: isCommentLine,
                        isInString: isInString
                    });
                }
                
                return decorations;
            }
            
            function isPositionInStringWorker(pos, stringRanges) {
                for (const range of stringRanges) {
                    if (pos.line === range.start.line && 
                        pos.char >= range.start.char && 
                        pos.char <= range.end.char) {
                        return true;
                    }
                }
                return false;
            }
        `;
        
        const blob = new Blob([workerCode], { type: 'application/javascript' });
        semanticWorker = new Worker(URL.createObjectURL(blob));
        
        console.log('Semantic coloring Web Worker initialized');
    } catch (error) {
        console.warn('Failed to create semantic worker:', error);
        semanticWorker = null;
    }
}

/**
 * 获取语义令牌数据（带缓存）
 */
async function getSemanticTokens(model) {
    const file = getCurrentFile();
    const packages = file?.nugetConfig?.packages || [];
    const code = model.getValue();
    const packagesData = packages.map(p => ({
        Id: p.id,
        Version: p.version
    }));

    const useMultiFile = shouldUseMultiFileMode(code);
    let cacheKey;
    let endpoint;
    let requestPayload;

    if (useMultiFile) {
        const multiFileRequest = createMultiFileRequest(file?.name, undefined, packagesData, code);
        if (!multiFileRequest) {
            return { data: new Uint32Array(0) };
        }

        const fingerprint = multiFileRequest.Files
            .map(f => `${f.FileName}:${hashCode(f.Content)}`)
            .sort()
            .join('|');

        cacheKey = hashCode(`mf:${multiFileRequest.TargetFileId}|${fingerprint}|${JSON.stringify(packagesData)}`);
        endpoint = "multiFileSemanticTokens";
        requestPayload = multiFileRequest;
    } else {
        cacheKey = hashCode(`sf:${code}|${JSON.stringify(packagesData)}`);
        endpoint = "semanticTokens";
        requestPayload = {
            Code: code,
            Packages: packagesData
        };
    }

    if (tokenCache.has(cacheKey)) {
        return tokenCache.get(cacheKey);
    }

    try {
        const { data } = await sendRequest(endpoint, requestPayload);
        if (data && data.data) {
            const result = { data: new Uint32Array(data.data) };

            if (tokenCache.size > 50) {
                const firstKey = tokenCache.keys().next().value;
                tokenCache.delete(firstKey);
            }
            tokenCache.set(cacheKey, result);

            return result;
        }
    } catch (error) {
        console.error('Failed to get semantic tokens:', error);
    }

    const emptyResult = { data: new Uint32Array(0) };
    tokenCache.set(cacheKey, emptyResult);
    return emptyResult;
}

/**
 * 设置自动语义着色（使用装饰器作为备选方案）
 */
function setupAutoSemanticColoring(legend) {
    // 监听编辑器创建
    monaco.editor.onDidCreateModel((model) => {
        if (model.getLanguageId() === 'csharp') {
            setupModelSemanticColoring(model, legend);
        }
    });

    // 为现有编辑器设置
    const editors = monaco.editor.getEditors();
    editors.forEach(editor => {
        const model = editor.getModel();
        if (model && model.getLanguageId() === 'csharp') {
            setupModelSemanticColoring(model, legend);
        }
    });
}

/**
 * 为模型设置语义着色
 */
function setupModelSemanticColoring(model, legend) {
    // 存储每个编辑器的装饰器ID和缓存
    const decorationIds = new Map();
    let lastContentHash = '';
    let isProcessing = false;
    let requestId = 0;
    
    const applyColoring = async () => {
        if (isProcessing) return;
        
        const currentRequestId = ++requestId;
        
        try {
            isProcessing = true;
            
            // 快速检查内容是否有变化
            const currentContent = model.getValue();
            const currentHash = hashCode(currentContent);
            
            if (currentHash === lastContentHash) {
                return; // 内容没有变化，跳过处理
            }
            
            // 分批处理大文件
            if (currentContent.length > 10000) {
                await applyColoringBatched(model, legend, decorationIds, currentRequestId);
            } else {
                const tokens = await getSemanticTokens(model);
                
                // 检查请求是否已过期
                if (currentRequestId !== requestId) return;
                
                if (tokens.data && tokens.data.length > 0) {
                    applySemanticDecorationsFast(model, tokens.data, legend, decorationIds);
                    lastContentHash = currentHash;
                } else {
                    // 如果没有语义令牌，清理所有装饰器
                    clearAllDecorations(model, decorationIds);
                    lastContentHash = '';
                }
            }
        } catch (error) {
            console.error('Failed to apply semantic coloring:', error);
        } finally {
            isProcessing = false;
        }
    };

    // 初始应用（进一步减少延迟）
    setTimeout(applyColoring, 100);

    // 内容变化时重新应用（更快响应）
    let timeoutHandle = null;
    model.onDidChangeContent(() => {
        clearTimeout(timeoutHandle);
        timeoutHandle = setTimeout(applyColoring, 300);
    });
}

/**
 * 超快速装饰器应用（使用 Worker 或优化算法）
 */
async function applySemanticDecorationsFast(model, tokenData, legend, decorationIds) {
    const lines = model.getLinesContent();
    const commentLines = new Set();
    
    // 极速注释行扫描
    for (let i = 0; i < lines.length; i++) {
        if (lines[i].includes('//')) {
            commentLines.add(i + 1);
        }
    }

    // 如果有 Worker 且数据量大，使用 Worker 处理
    if (semanticWorker && tokenData.length > 1000) {
        const stringRanges = detectStringRanges(model);
        
        return new Promise((resolve) => {
            const handleWorkerMessage = (e) => {
                if (e.data.action === 'tokenProcessed') {
                    semanticWorker.removeEventListener('message', handleWorkerMessage);
                    const processedTokens = e.data.result;
                    applyProcessedDecorations(model, processedTokens, legend, decorationIds);
                    resolve();
                }
            };
            
            semanticWorker.addEventListener('message', handleWorkerMessage);
            semanticWorker.postMessage({
                action: 'processTokens',
                data: { 
                    tokenData: Array.from(tokenData), 
                    commentLines: Array.from(commentLines),
                    stringRanges: stringRanges
                }
            });
        });
    }
    
    // 回退到主线程处理（超优化版本）
    const decorations = [];
    let currentLine = 0;
    let currentChar = 0;
    
    // 检测字符串范围以避免在字符串内部应用语义着色
    const stringRanges = detectStringRanges(model);
    
    // 只处理关键令牌类型，添加更多重要的类型
    const importantTypes = new Set(['class', 'interface', 'method', 'function', 'comment', 'type', 'struct', 'enum']);
    
    for (let i = 0; i < tokenData.length; i += 5) {
        currentLine += tokenData[i];
        currentChar = tokenData[i] === 0 ? currentChar + tokenData[i + 1] : tokenData[i + 1];
        
        const tokenType = legend.tokenTypes[tokenData[i + 3]];
        if (!tokenType || !importantTypes.has(tokenType)) continue;
        
        // 更精确的注释检测
        const lineContent = model.getLineContent(currentLine + 1);
        const commentStart = lineContent.indexOf('//');
        const isInComment = commentStart !== -1 && currentChar >= commentStart;
        
        // 检查是否在字符串内部
        const tokenPos = { line: currentLine + 1, char: currentChar };
        const isInString = isPositionInString(tokenPos, stringRanges);
        
        // 如果在字符串内部，跳过语义着色（除非是字符串本身）
        if (isInString && tokenType !== 'string') {
            continue;
        }
        
        if (isInComment || tokenType === 'comment') {
            decorations.push({
                range: new monaco.Range(
                    currentLine + 1, currentChar + 1,
                    currentLine + 1, currentChar + tokenData[i + 2] + 1
                ),
                options: { inlineClassName: 'semantic-token-comment-override' }
            });
        } else {
            decorations.push({
                range: new monaco.Range(
                    currentLine + 1, currentChar + 1,
                    currentLine + 1, currentChar + tokenData[i + 2] + 1
                ),
                options: { inlineClassName: getTokenClassName(tokenType, tokenData[i + 4]) }
            });
        }
    }
    
    // 立即应用装饰器
    const editors = monaco.editor.getEditors();
    editors.forEach(editor => {
        if (editor.getModel() === model) {
            const oldDecorationIds = decorationIds.get(editor) || [];
            const newDecorationIds = editor.deltaDecorations(oldDecorationIds, decorations);
            decorationIds.set(editor, newDecorationIds);
        }
    });
}

/**
 * 应用 Worker 处理后的装饰器
 */
function applyProcessedDecorations(model, processedTokens, legend, decorationIds) {
    const decorations = processedTokens
        .filter(token => {
            const tokenType = legend.tokenTypes[token.typeIndex];
            // 过滤掉字符串内部的非字符串令牌
            if (token.isInString && tokenType !== 'string') {
                return false;
            }
            // 只处理重要的令牌类型
            return ['class', 'interface', 'method', 'function', 'comment', 'string', 'type', 'struct', 'enum'].includes(tokenType);
        })
        .map(token => {
            const tokenType = legend.tokenTypes[token.typeIndex];
            const className = token.isComment ? 'semantic-token-comment-override' : getTokenClassName(tokenType, token.modifiers);
            
            return {
                range: new monaco.Range(token.line, token.char, token.line, token.char + token.length),
                options: { inlineClassName: className }
            };
        });
    
    const editors = monaco.editor.getEditors();
    editors.forEach(editor => {
        if (editor.getModel() === model) {
            const oldDecorationIds = decorationIds.get(editor) || [];
            const newDecorationIds = editor.deltaDecorations(oldDecorationIds, decorations);
            decorationIds.set(editor, newDecorationIds);
        }
    });
}

/**
 * 分批处理大文件的着色
 */
async function applyColoringBatched(model, legend, decorationIds, requestId) {
    const tokens = await getSemanticTokens(model);
    
    if (requestId !== requestId || !tokens.data || tokens.data.length === 0) return;
    
    // 只处理可视区域附近的内容
    const editors = monaco.editor.getEditors();
    const editor = editors.find(e => e.getModel() === model);
    if (!editor) return;
    
    const visibleRange = editor.getVisibleRanges()[0];
    if (!visibleRange) return;
    
    const startLine = Math.max(1, visibleRange.startLineNumber - 50);
    const endLine = Math.min(model.getLineCount(), visibleRange.endLineNumber + 50);
    
    // 只为可视区域应用装饰器
    applySemanticDecorationsInRange(model, tokens.data, legend, decorationIds, startLine, endLine);
}

/**
 * 在指定范围内应用装饰器
 */
function applySemanticDecorationsInRange(model, tokenData, legend, decorationIds, startLine, endLine) {
    const decorations = [];
    let currentLine = 0;
    let currentChar = 0;

    // 解析语义令牌数据，只处理指定范围
    for (let i = 0; i < tokenData.length; i += 5) {
        const deltaLine = tokenData[i];
        const deltaChar = tokenData[i + 1];
        const length = tokenData[i + 2];
        const tokenTypeIndex = tokenData[i + 3];
        const tokenModifiers = tokenData[i + 4];

        currentLine += deltaLine;
        currentChar = deltaLine === 0 ? currentChar + deltaChar : deltaChar;
        
        // 跳过不在可视范围内的令牌
        if (currentLine + 1 < startLine) continue;
        if (currentLine + 1 > endLine) break;

        const tokenType = legend.tokenTypes[tokenTypeIndex];
        if (tokenType && ['class', 'interface', 'method', 'function', 'comment'].includes(tokenType)) {
            decorations.push({
                range: new monaco.Range(
                    currentLine + 1, currentChar + 1,
                    currentLine + 1, currentChar + length + 1
                ),
                options: { inlineClassName: getTokenClassName(tokenType, tokenModifiers) }
            });
        }
    }
    
    // 应用装饰器
    const editors = monaco.editor.getEditors();
    editors.forEach(editor => {
        if (editor.getModel() === model) {
            const oldDecorationIds = decorationIds.get(editor) || [];
            const newDecorationIds = editor.deltaDecorations(oldDecorationIds, decorations);
            decorationIds.set(editor, newDecorationIds);
        }
    });
}

/**
 * 清理所有装饰器
 */
function clearAllDecorations(model, decorationIds) {
    const editors = monaco.editor.getEditors();
    editors.forEach(editor => {
        if (editor.getModel() === model) {
            const oldDecorationIds = decorationIds.get(editor) || [];
            editor.deltaDecorations(oldDecorationIds, []);
            decorationIds.delete(editor);
        }
    });
}

/**
 * 快速哈希函数
 */
function hashCode(str) {
    let hash = 0;
    if (str.length === 0) return hash;
    for (let i = 0; i < str.length; i++) {
        const char = str.charCodeAt(i);
        hash = ((hash << 5) - hash) + char;
        hash = hash & hash; // 转为32位整数
    }
    return hash;
}

/**
 * 获取注释范围
 */
function getCommentRanges(model) {
    const content = model.getValue();
    const lines = content.split('\n');
    const ranges = [];
    let inBlockComment = false;
    let blockCommentStart = null;
    
    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const lineNumber = i + 1;
        
        // 处理块注释
        if (!inBlockComment) {
            const blockStart = line.indexOf('/*');
            if (blockStart !== -1) {
                inBlockComment = true;
                blockCommentStart = { line: lineNumber, char: blockStart };
            }
        }
        
        if (inBlockComment) {
            const blockEnd = line.indexOf('*/');
            if (blockEnd !== -1) {
                ranges.push({
                    type: 'block',
                    start: blockCommentStart,
                    end: { line: lineNumber, char: blockEnd + 2 }
                });
                inBlockComment = false;
                blockCommentStart = null;
            }
        }
        
        // 处理单行注释
        if (!inBlockComment) {
            const commentStart = line.indexOf('//');
            if (commentStart !== -1) {
                ranges.push({
                    type: 'line',
                    start: { line: lineNumber, char: commentStart },
                    end: { line: lineNumber, char: line.length }
                });
            }
        }
    }
    
    return ranges;
}

/**
 * 检查位置是否在注释中
 */
function isPositionInComment(pos, commentRanges) {
    for (const range of commentRanges) {
        if (range.type === 'line') {
            if (pos.line === range.start.line && pos.char >= range.start.char) {
                return true;
            }
        } else if (range.type === 'block') {
            if ((pos.line > range.start.line || (pos.line === range.start.line && pos.char >= range.start.char)) &&
                (pos.line < range.end.line || (pos.line === range.end.line && pos.char <= range.end.char))) {
                return true;
            }
        }
    }
    return false;
}

/**
 * 检测字符串范围
 */
function detectStringRanges(model) {
    const content = model.getValue();
    const lines = content.split('\n');
    const ranges = [];
    
    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const lineNumber = i + 1;
        let inString = false;
        let stringStart = -1;
        let escapeNext = false;
        let stringChar = '';
        
        // 先检查是否是注释行，如果是注释行则跳过字符串检测
        const trimmedLine = line.trim();
        if (trimmedLine.startsWith('//')) {
            continue;
        }
        
        for (let j = 0; j < line.length; j++) {
            const char = line[j];
            
            // 如果遇到注释开始，停止字符串检测
            if (!inString && j < line.length - 1 && char === '/' && line[j + 1] === '/') {
                break;
            }
            
            if (escapeNext) {
                escapeNext = false;
                continue;
            }
            
            if (char === '\\' && inString) {
                escapeNext = true;
                continue;
            }
            
            if (!inString && (char === '"' || char === "'" || char === '`')) {
                inString = true;
                stringStart = j;
                stringChar = char;
            } else if (inString && char === stringChar) {
                ranges.push({
                    start: { line: lineNumber, char: stringStart },
                    end: { line: lineNumber, char: j }
                });
                inString = false;
                stringStart = -1;
                stringChar = '';
            }
        }
        
        // 处理未闭合的字符串（延续到行尾）
        if (inString) {
            ranges.push({
                start: { line: lineNumber, char: stringStart },
                end: { line: lineNumber, char: line.length }
            });
        }
    }
    
    return ranges;
}

/**
 * 检查位置是否在字符串中
 */
function isPositionInString(pos, stringRanges) {
    for (const range of stringRanges) {
        if (pos.line === range.start.line && 
            pos.char >= range.start.char && 
            pos.char <= range.end.char) {
            return true;
        }
    }
    return false;
}

/**
 * 优化的注释行覆盖样式添加
 */
function addCommentLineOverridesFast(model, decorations, commentRanges) {
    for (const range of commentRanges) {
        if (range.type === 'line') {
            const lineContent = model.getLineContent(range.start.line);
            const lineLength = lineContent.length;
            
            if (lineLength > range.start.char + 2) {
                decorations.push({
                    range: new monaco.Range(
                        range.start.line, range.start.char + 1,
                        range.start.line, lineLength + 1
                    ),
                    options: { 
                        inlineClassName: 'semantic-token-comment-override',
                        minimap: { color: '#57A64A' }
                    }
                });
            }
        }
    }
}

/**
 * 获取令牌的 CSS 类名
 */
function getTokenClassName(tokenType, modifiers) {
    let className = `semantic-token-${tokenType}`;
    
    // 抽象成员稍微透明
    if (modifiers & 32) {
        className += ' semantic-abstract';
    }
    
    return className;
}

/**
 * 添加 Visual Studio 风格的样式
 */
function addVSStyles() {
    const style = document.createElement('style');
    style.textContent = `
        /* Visual Studio Dark 语义着色样式 */
        body.theme-dark .semantic-token-class { color: #4EC9B0 !important; }
        body.theme-dark .semantic-token-interface { color: #4EC9B0 !important; }
        body.theme-dark .semantic-token-struct { color: #4EC9B0 !important; }
        body.theme-dark .semantic-token-enum { color: #4EC9B0 !important; }
        
        body.theme-dark .semantic-token-method { color: #DCDCAA !important; }
        body.theme-dark .semantic-token-function { color: #DCDCAA !important; }
        
        body.theme-dark .semantic-token-property { color: #9CDCFE !important; }
        body.theme-dark .semantic-token-variable { color: #9CDCFE !important; }
        body.theme-dark .semantic-token-parameter { color: #9CDCFE !important; }
        
        body.theme-dark .semantic-token-event { color: #9CDCFE !important; }
        body.theme-dark .semantic-token-enumMember { color: #B5CEA8 !important; }
        body.theme-dark .semantic-token-namespace { color: #FFFFFF !important; }
        
        body.theme-dark .semantic-token-keyword { color: #569CD6 !important; }
        body.theme-dark .semantic-token-string { color: #CE9178 !important; }
        body.theme-dark .semantic-token-number { color: #B5CEA8 !important; }
        body.theme-dark .semantic-token-comment { color: #57A64A !important; }
        body.theme-dark .semantic-token-comment-override { 
            color: #57A64A !important; 
            font-style: inherit !important;
            font-weight: inherit !important;
        }
        body.theme-dark .semantic-token-operator { color: #D4D4D4 !important; }
        body.theme-dark .semantic-token-modifier { color: #569CD6 !important; }
        
        /* Visual Studio 2022 Light 语义着色样式 */
        body.theme-light .semantic-token-class { color: #2B91AF !important; }
        body.theme-light .semantic-token-interface { color: #2B91AF !important; }
        body.theme-light .semantic-token-struct { color: #2B91AF !important; }
        body.theme-light .semantic-token-enum { color: #2B91AF !important; }
        
        body.theme-light .semantic-token-method { color: #795E26 !important; }
        body.theme-light .semantic-token-function { color: #795E26 !important; }
        
        body.theme-light .semantic-token-property { color: #001080 !important; }
        body.theme-light .semantic-token-variable { color: #001080 !important; }
        body.theme-light .semantic-token-parameter { color: #001080 !important; }
        
        body.theme-light .semantic-token-event { color: #001080 !important; }
        body.theme-light .semantic-token-enumMember { color: #0451A5 !important; }
        body.theme-light .semantic-token-namespace { color: #000000 !important; }
        
        body.theme-light .semantic-token-keyword { color: #0000FF !important; }
        body.theme-light .semantic-token-string { color: #A31515 !important; }
        body.theme-light .semantic-token-number { color: #098658 !important; }
        body.theme-light .semantic-token-comment { color: #008000 !important; }
        body.theme-light .semantic-token-comment-override { 
            color: #008000 !important; 
            font-style: inherit !important;
            font-weight: inherit !important;
        }
        body.theme-light .semantic-token-operator { color: #000000 !important; }
        body.theme-light .semantic-token-modifier { color: #0000FF !important; }
        
        .semantic-abstract { opacity: 0.9 !important; }
    `;
    document.head.appendChild(style);
}

// 开发调试函数
if (typeof window !== 'undefined') {
    window.applySemanticColoringNow = async function() {
        const startTime = performance.now();
        const editors = monaco.editor.getEditors();
        const decorationIds = new Map();
        
        for (const editor of editors) {
            const model = editor.getModel();
            if (model && model.getLanguageId() === 'csharp') {
                const tokens = await getSemanticTokens(model);
                if (tokens.data && tokens.data.length > 0) {
                    const legend = {
                        tokenTypes: [
                            'namespace', 'class', 'enum', 'interface', 'struct', 'typeParameter',
                            'parameter', 'variable', 'property', 'enumMember', 'event',
                            'function', 'method', 'macro', 'keyword', 'modifier',
                            'comment', 'string', 'number', 'regexp', 'operator'
                        ]
                    };
                    await applySemanticDecorationsFast(model, tokens.data, legend, decorationIds);
                }
            }
        }
        const endTime = performance.now();
        console.log(`🚀 Ultra-fast semantic coloring applied in ${(endTime - startTime).toFixed(2)}ms`);
        console.log(`Worker available: ${semanticWorker ? '✅' : '❌'}`);
    };
    
    // 缓存统计
    window.getSemanticColoringStats = function() {
        console.log('📊 Semantic Coloring Stats:');
        console.log('  Token cache size:', tokenCache.size);
        console.log('  Worker available:', semanticWorker ? 'Yes' : 'No');
        console.log('  Recent cache keys:', Array.from(tokenCache.keys()).slice(-3));
    };
    
    // 清理缓存
    window.clearSemanticColoringCache = function() {
        tokenCache.clear();
        console.log('🧹 Semantic coloring cache cleared');
    };
    
    // 性能测试
    window.testSemanticColoringPerformance = async function() {
        const iterations = 5;
        const times = [];
        
        for (let i = 0; i < iterations; i++) {
            const start = performance.now();
            await window.applySemanticColoringNow();
            const end = performance.now();
            times.push(end - start);
        }
        
        const avg = times.reduce((a, b) => a + b, 0) / times.length;
        console.log(`⚡ Performance Test Results (${iterations} iterations):`);
        console.log(`  Average time: ${avg.toFixed(2)}ms`);
        console.log(`  Min time: ${Math.min(...times).toFixed(2)}ms`);
        console.log(`  Max time: ${Math.max(...times).toFixed(2)}ms`);
    };
    
    // 调试字符串检测
    window.debugStringDetection = function() {
        const editors = monaco.editor.getEditors();
        const editor = editors[0];
        if (!editor) return;
        
        const model = editor.getModel();
        const stringRanges = detectStringRanges(model);
        
        console.log('🔍 String Detection Debug:');
        console.log('String ranges found:', stringRanges);
        
        stringRanges.forEach((range, index) => {
            const content = model.getValueInRange(new monaco.Range(
                range.start.line, range.start.char + 1,
                range.end.line, range.end.char + 1
            ));
            console.log(`  String ${index + 1}: "${content}" at line ${range.start.line}, chars ${range.start.char}-${range.end.char}`);
        });
    };
}
