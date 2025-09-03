import { getCurrentFile } from './utils/common.js';
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

/**
 * 获取语义令牌数据
 */
async function getSemanticTokens(model) {
    const file = getCurrentFile();
    const packages = file?.nugetConfig?.packages || [];

    const request = {
        Code: model.getValue(),
        Packages: packages.map(p => ({
            Id: p.id,
            Version: p.version
        }))
    };

    try {
        const { data } = await sendRequest("semanticTokens", request);
        if (data && data.data) {
            return { data: new Uint32Array(data.data) };
        }
    } catch (error) {
        console.error('Failed to get semantic tokens:', error);
    }

    return { data: new Uint32Array(0) };
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
    const applyColoring = async () => {
        try {
            const tokens = await getSemanticTokens(model);
            if (tokens.data && tokens.data.length > 0) {
                applySemanticDecorations(model, tokens.data, legend);
            }
        } catch (error) {
            console.error('Failed to apply semantic coloring:', error);
        }
    };

    // 初始应用
    setTimeout(applyColoring, 500);

    // 内容变化时重新应用
    let timeoutHandle = null;
    model.onDidChangeContent(() => {
        clearTimeout(timeoutHandle);
        timeoutHandle = setTimeout(applyColoring, 1500);
    });
}

/**
 * 应用语义装饰器
 */
function applySemanticDecorations(model, tokenData, legend) {
    const decorations = [];
    let currentLine = 0;
    let currentChar = 0;

    // 解析语义令牌数据
    for (let i = 0; i < tokenData.length; i += 5) {
        const deltaLine = tokenData[i];
        const deltaChar = tokenData[i + 1];
        const length = tokenData[i + 2];
        const tokenTypeIndex = tokenData[i + 3];
        const tokenModifiers = tokenData[i + 4];

        currentLine += deltaLine;
        currentChar = deltaLine === 0 ? currentChar + deltaChar : deltaChar;

        const tokenType = legend.tokenTypes[tokenTypeIndex];
        if (tokenType) {
            const className = getTokenClassName(tokenType, tokenModifiers);
            decorations.push({
                range: new monaco.Range(
                    currentLine + 1, currentChar + 1,
                    currentLine + 1, currentChar + length + 1
                ),
                options: { inlineClassName: className }
            });
        }
    }

    // 应用到编辑器
    const editors = monaco.editor.getEditors();
    editors.forEach(editor => {
        if (editor.getModel() === model) {
            editor.deltaDecorations([], decorations);
        }
    });
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
        /* Visual Studio 语义着色样式 */
        .semantic-token-class { color: #4EC9B0 !important; }
        .semantic-token-interface { color: #B8D7A3 !important; }
        .semantic-token-struct { color: #86C691 !important; }
        .semantic-token-enum { color: #B8D7A3 !important; }
        
        .semantic-token-method { color: #DCDCAA !important; }
        .semantic-token-function { color: #DCDCAA !important; }
        
        .semantic-token-property { color: #FFFFFF !important; }
        .semantic-token-variable { color: #9CDCFE !important; }
        .semantic-token-parameter { color: #D4D4D4 !important; }
        
        .semantic-token-event { color: #FFD700 !important; }
        .semantic-token-enumMember { color: #B5CEA8 !important; }
        .semantic-token-namespace { color: #FFFFFF !important; }
        
        .semantic-abstract { opacity: 0.9 !important; }
    `;
    document.head.appendChild(style);
}

// 开发调试函数
if (typeof window !== 'undefined') {
    window.applySemanticColoringNow = function() {
        const editors = monaco.editor.getEditors();
        editors.forEach(async (editor) => {
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
                    applySemanticDecorations(model, tokens.data, legend);
                }
            }
        });
        console.log('Semantic coloring applied manually');
    };
}