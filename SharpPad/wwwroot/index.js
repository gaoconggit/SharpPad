// Monaco Editor 配置
const monacoConfig = {
    paths: { 
        'vs': 'monaco-editor/min/vs'
    }
};

// 动态加载 Monaco Editor
function loadMonaco() {
    return new Promise((resolve) => {
        const script = document.createElement('script');
        script.src = 'monaco-editor/min/vs/loader.js';
        script.onload = () => {
            require.config(monacoConfig);
            require(['vs/editor/editor.main'], () => {
                resolve();
            });
        };
        document.head.appendChild(script);
    });
}

import { Editor } from './editor/editor.js';
import { EditorCommands } from './editor/commands.js';
import { FileManager } from './fileSystem/fileManager.js';
import { registerCsharpProvider } from './csharpLanguageProvider.js';
import { CodeRunner } from './execution/runner.js';
import { OutputPanel } from './execution/outputPanel.js';
import { FileListResizer } from './fileSystem/fileListResizer.js';

// 初始化应用
async function initializeApp() {
    // 初始化文件系统
    const fileManager = new FileManager();
    fileManager.loadFileList();

    // 等待 Monaco Editor 加载完成
    await loadMonaco();
    
    // 注册C#语言支持
    registerCsharpProvider();

    // 初始化编辑器
    const editorInstance = new Editor();
    const editor = editorInstance.initialize('container');
    
    // 注册编辑器命令
    const commands = new EditorCommands(editor);
    commands.registerCommands();

    // 初始化代码运行器和输出面板
    const outputPanel = new OutputPanel();
    const codeRunner = new CodeRunner();

    // 将编辑器实例暴露给全局，以便其他模块使用
    window.editor = editor;
}

// 启动应用
initializeApp();