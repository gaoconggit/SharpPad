require.config({ paths: { vs: 'node_modules/monaco-editor/min/vs' } });

require(['vs/editor/editor.main'], function () {

    registerCsharpProvider();

    var editor = monaco.editor.create(document.getElementById('container'), {
        value: `using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;

class Program
{
    public static async Task Main()
    {
         Console.WriteLine("Hello, World!");
    }
}`,
        language: 'csharp',
        theme: "vs-dark"
    });

    editor.addCommand(
        monaco.KeyMod.chord(
            monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyK,
            monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyD
        ),
        () => {
            editor.getAction('editor.action.formatDocument').run();
        }
    );

    editor.addCommand(
        monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter,
        async () => {
            if (!editor || !editor.getValue()) return;
            await runCode(editor.getValue());
        }
    );

    //添加 ctrl + j 触发智能提示,现在 ctl + 空格可以触发能提示
    editor.addCommand(
        monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyJ,
        () => {
            editor.trigger('keyboard', 'editor.action.triggerSuggest', {});
        }
    );

    editor.addCommand(
        monaco.KeyMod.chord(
            monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS
        ),
        () => {
            saveCode(editor.getValue());
        }
    );

    fullScreen(editor);

    document.getElementById('runButton').addEventListener('click', async () => {
        if (!editor || !editor.getValue()) return;


        await runCode(editor.getValue());
    });

    document.getElementById('clearOutput').addEventListener('click', () => {
        document.getElementById('outputContent').innerHTML = '';
    });

    document.getElementById('formatOutput').addEventListener('click', () => {
        const outputContent = document.getElementById('outputContent');
        const lines = Array.from(outputContent.children);

        lines.forEach(line => {
            try {
                // 保存原始的类名
                const originalClass = line.className;

                // 尝试格式化内容
                const formattedText = formatJSON(line.textContent);
                if (formattedText !== line.textContent) {
                    // 创建新的 pre 元素来保持格式
                    const pre = document.createElement('pre');
                    pre.textContent = formattedText;
                    pre.className = originalClass;
                    pre.style.margin = '0';
                    pre.style.fontFamily = 'Consolas, monospace';
                    pre.style.whiteSpace = 'pre-wrap';  // 添加这行以保格式自动换行

                    // 替换原始元素
                    line.parentNode.replaceChild(pre, line);
                }
            } catch (e) {
                console.error('Format error:', e);
            }
        });
    });

    window.x_editor = editor;

    // 在编辑器初始化完成后，设置焦点
    editor.focus();

    // 初始化输出面板调整小功能
    const outputPanel = document.getElementById('outputPanel');
    const resizeHandle = outputPanel.querySelector('.resize-handle');
    let isResizing = false;
    let startY;
    let startHeight;

    resizeHandle.addEventListener('mousedown', function (e) {
        isResizing = true;
        startY = e.clientY;
        startHeight = parseInt(document.defaultView.getComputedStyle(outputPanel).height, 10);

        document.documentElement.style.cursor = 'ns-resize';
        e.preventDefault(); // Prevent text selection
    });

    document.addEventListener('mousemove', function (e) {
        if (!isResizing) return;

        const delta = e.clientY - startY;
        const newHeight = Math.min(Math.max(startHeight - delta, 100), window.innerHeight * 0.8);
        outputPanel.style.height = `${newHeight}px`;

        // 调整编辑器大小以适应新的输出面板高度
        editor.layout();
    });

    document.addEventListener('mouseup', function () {
        if (isResizing) {
            isResizing = false;
            document.documentElement.style.cursor = '';
        }
    });

    // Prevent text selection while resizing
    document.addEventListener('selectstart', function (e) {
        if (isResizing) {
            e.preventDefault();
        }
    });

    // 修改编辑器命令绑定
    editor.addCommand(
        monaco.KeyMod.Alt | monaco.KeyCode.KeyC,
        () => {
            document.getElementById('outputContent').innerHTML = '';
        }
    );

    // 或者使用另一方式绑定快捷键
    editor.addAction({
        id: 'clearOutput',
        label: '清除输出',
        keybindings: [monaco.KeyMod.Alt | monaco.KeyCode.KeyC],
        run: () => {
            document.getElementById('outputContent').innerHTML = '';
            return null;
        }
    });

    // 保留全局快捷键处理
    window.addEventListener('keydown', (e) => {
        if (e.altKey && e.key.toLowerCase() === 'c') {
            e.preventDefault();
            document.getElementById('outputContent').innerHTML = '';
        }
    }, true);

    // 触发一次布局更新，确保编辑器正确渲染
    editor.layout();
});

function fullScreen(editor) {
    const style = document.createElement('style');
    style.textContent = `
.fullscreen-editor {
position: fixed !important;
top: 0 !important;
left: 0 !important;
width: 100vw !important;
height: 100vh !important;
z-index: 9999 !important;
padding: 0 !important;
margin: 0 !important;
}

.fullscreen-editor #container {
position: fixed !important;
top: 0 !important;
left: 0 !important;
width: 100vw !important;
height: 100vh !important;
margin: 0 !important;
padding: 0 !important;
z-index: 9999 !important;
transform: none !important;
}

.fullscreen-editor .monaco-editor {
width: 100% !important;
height: 100% !important;
margin: 0 !important;
padding: 0 !important;
}

.fullscreen-editor .monaco-editor .overflow-guard {
width: 100% !important;
height: 100% !important;
}

.fullscreen-button {
position: fixed;
top: 12px;
right: 15px;
z-index: 10000;
width: 36px;
height: 36px;
padding: 0;
border: none;
border-radius: 4px;
background-color: #333;
color: #fff;
font-size: 20px;
cursor: pointer;
display: flex;
align-items: center;
justify-content: center;
opacity: 0.6;
transition: all 0.2s ease;
box-shadow: 0 2px 6px rgba(0, 0, 0, 0.2);
}

.fullscreen-button:hover {
opacity: 1;
background-color: #444;
box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
transform: translateY(-1px);
}

.fullscreen-button:active {
transform: translateY(1px);
box-shadow: 0 1px 3px rgba(0, 0, 0, 0.2);
}
`;
    document.head.appendChild(style);

    const fullscreenButton = document.createElement('button');
    fullscreenButton.className = 'fullscreen-button';
    fullscreenButton.innerHTML = '⛶';
    fullscreenButton.title = '全屏';

    document.body.appendChild(fullscreenButton);

    let isFullscreen = false;
    fullscreenButton.addEventListener('click', () => {
        isFullscreen = !isFullscreen;
        const editorContainer = editor.getDomNode().parentElement;
        if (isFullscreen) {
            editorContainer.classList.add('fullscreen-editor');
            fullscreenButton.innerHTML = '⮌';
            fullscreenButton.title = '退出全屏';
        } else {
            editorContainer.classList.remove('fullscreen-editor');
            fullscreenButton.innerHTML = '⛶';
            fullscreenButton.title = '全屏';
        }
        setTimeout(function () {
            editor.layout();
        }, 100);
    });

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && isFullscreen) {
            isFullscreen = false;
            const editorContainer = editor.getDomNode().parentElement;
            editorContainer.classList.remove('fullscreen-editor');
            fullscreenButton.innerHTML = '⛶';
            fullscreenButton.title = '全屏';
            editor.layout();
        }
    });
}

function appendOutput(message, type = 'info') {
    const outputContent = document.getElementById('outputContent');
    const outputLine = document.createElement('div');
    outputLine.className = `output-${type}`;

    // 尝试自动格式化 JSON
    const formattedMessage = formatJSON(message);
    if (formattedMessage !== message) {
        // 如果是 JSON，使用 pre 元素来保持格式
        const pre = document.createElement('pre');
        pre.textContent = formattedMessage;
        pre.style.margin = '0';
        pre.style.fontFamily = 'Consolas, monospace';
        pre.style.whiteSpace = 'pre-wrap';
        outputLine.appendChild(pre);
    } else {
        // 如果不是 JSON，也使用 pre 元素来保持格式
        const pre = document.createElement('pre');
        pre.textContent = message;
        pre.style.margin = '0';
        pre.style.fontFamily = 'Consolas, monospace';
        pre.style.whiteSpace = 'pre-wrap';
        outputLine.appendChild(pre);
    }

    outputContent.appendChild(outputLine);
    outputContent.scrollTop = outputContent.scrollHeight;
}

// 流式输出
function streamOutput(message, type = 'info') {
    const md = markdownit({
        highlight: function (str, lang) {
            if (lang && hljs.getLanguage(lang)) {
                try {
                    return `<pre class="hljs"><code>${hljs.highlight(str, { language: lang }).value}</code></pre>`;
                } catch (_) {
                    return `<pre class="hljs"><code>${md.utils.escapeHtml(str)}</code></pre>`;
                }
            } else {
                // 如果语言不存在或者无法识别，使用自动检测
                return `<pre class="hljs"><code>${hljs.highlightAuto(str).value}</code></pre>`;
            }
        }
    });

    const outputDiv = document.getElementById('outputContent');
    outputDiv.classList.add("result-streaming");
    const cursor = document.createElement("p");
    outputDiv.appendChild(cursor);

    message = message.replace(/\r\n/g, '\n\r\n');
    outputDiv.innerHTML = md.render(message);
}

async function runCode(code) {
    if (!code) return;

    //运行代码之前如果已选择文件则自动保存
    const fileId = document.querySelector('#fileListItems a.selected')?.getAttribute('data-file-id');
    if (fileId) {
        // 保存到 localStorage
        localStorage.setItem(`file_${fileId}`, x_editor.getValue());
    }

    // 获取当前文件的包配置
    const file = getCurrentFile();
    const packages = file?.nugetConfig?.packages || [];

    const request = {
        SourceCode: code,
        Assemblies: [],
        Packages: packages.map(p => ({
            Id: p.id,
            Version: p.version
        }))
    };

    const notification = document.getElementById('notification');
    const outputContent = document.getElementById('outputContent');

    // 清空输出区域
    outputContent.innerHTML = '';

    try {
        let result = "";
        const { reader, showNotificationTimer } = await sendRequest('run', request);
        while (true) {
            const { value, done } = await reader.read();
            if (done) break;

            // 将 Uint8Array 转换为字符串
            const text = new TextDecoder("utf-8").decode(value);
            // 处理每一行数据
            const lines = text.split('\n');

            for (const line of lines) {
                if (!line.trim()) continue;
                if (!line.startsWith('data: ')) continue;

                const data = JSON.parse(line.substring(6));
                switch (data.type) {
                    case 'output':
                        result += data.content;
                        streamOutput(result, 'success');
                        break;
                    case 'error':
                        appendOutput(data.content, 'error');
                        notification.textContent = '运行出错';
                        notification.style.backgroundColor = 'rgba(244, 67, 54, 0.9)';
                        notification.style.display = 'block';
                        break;
                    case 'completed':
                        clearTimeout(showNotificationTimer); // 清除显示通知的定时器
                        notification.style.display = 'none';
                        outputContent.classList.remove("result-streaming");
                        return;
                }
            }
        }
    } catch (error) {
        appendOutput('运行请求失败: ' + error.message, 'error');
        notification.textContent = '运行失败';
        notification.style.backgroundColor = 'rgba(244, 67, 54, 0.9)';
    }
}
function showNotification(message, type) {
    const notification = document.getElementById('notification');
    notification.textContent = message;
    notification.style.backgroundColor = type === 'success' ? 'rgba(76, 175, 80, 0.9)' : 'rgba(244, 67, 54, 0.9)';
    notification.style.width = '600px';
    notification.style.margin = '10px auto';
    notification.style.padding = '15px';
    notification.style.borderRadius = '8px';
    notification.style.boxShadow = '0 4px 6px rgba(0, 0, 0, 0.1)';
    notification.style.position = 'fixed';
    notification.style.left = '50%';
    notification.style.transform = 'translateX(-50%)';
    notification.style.zIndex = '1000';
    notification.style.textAlign = 'center';
    notification.style.fontWeight = 'bold';
    notification.style.display = 'block';
    notification.style.transition = 'opacity 0.3s ease-in-out';
    notification.style.opacity = '1';
    notification.style.maxHeight = '200px';
    notification.style.overflowY = 'auto';

    const closeButton = document.createElement('button');
    closeButton.innerHTML = '&times;';
    closeButton.style.background = 'none';
    closeButton.style.border = 'none';
    closeButton.style.color = '#fff';
    closeButton.style.fontSize = '30px';
    closeButton.style.position = 'absolute';
    closeButton.style.top = '2px';
    closeButton.style.right = '2px';
    closeButton.style.cursor = 'pointer';
    closeButton.addEventListener('click', () => {
        notification.style.display = 'none';
    });

    notification.appendChild(closeButton);

    // 如果是成功息，3秒后自动关闭
    if (type === 'success') {
        setTimeout(() => {
            notification.style.display = 'none';
        }, 3000);
    }
}

function filterFiles(filter) {
    // 滤文件列
    document.querySelectorAll('#fileListItems li').forEach(li => {
        if (li.textContent.includes(filter)) {
            li.style.display = 'block';
        } else {
            li.style.display = 'none';
        }
    });

    console.log(filter);
}

// 保存展开的文件夹状态
function saveExpandedFolders() {
    const expandedFolders = [];
    document.querySelectorAll('.folder-content.open').forEach(content => {
        const folderId = content.getAttribute('data-folder-content');
        if (folderId) {
            expandedFolders.push(folderId);
        }
    });
    return expandedFolders;
}

// 恢复文夹展开状态
function restoreExpandedFolders(expandedFolders) {
    expandedFolders.forEach(folderId => {
        const content = document.querySelector(`[data-folder-content="${folderId}"]`);
        const header = document.querySelector(`[data-folder-header="${folderId}"]`);
        if (content && header) {
            content.classList.add('open');
            header.classList.add('open');
        }
    });
}

// 修改 loadFileList 函数
function loadFileList() {
    const expandedFolders = saveExpandedFolders(); // 保存当前展开状态
    const filesData = localStorage.getItem('controllerFiles');
    if (filesData) {
        try {
            const files = JSON.parse(filesData);
            const fileList = document.getElementById('fileListItems');
            fileList.innerHTML = '';

            files.forEach(file => {
                const element = createFileElement(file);
                fileList.appendChild(element);
            });

            restoreExpandedFolders(expandedFolders); // 恢复展开状态
        } catch (error) {
            console.error('Error parsing files from localStorage:', error);
        }
    }
}

function createFileElement(file) {
    const li = document.createElement('li');

    if (file.type === 'folder') {
        // 创建文件夹结构
        const folderDiv = document.createElement('div');
        folderDiv.className = 'folder';

        const folderHeader = document.createElement('div');
        folderHeader.className = 'folder-header';
        folderHeader.textContent = file.name;
        folderHeader.setAttribute('data-folder-header', file.id);

        // 添加拖拽功能
        folderHeader.draggable = true;
        folderHeader.addEventListener('dragstart', (e) => {
            e.dataTransfer.setData('text/plain', file.id);
            e.dataTransfer.effectAllowed = 'move';
        });

        // 添加拖放目标功能
        folderHeader.addEventListener('dragover', (e) => {
            e.preventDefault();
            folderHeader.classList.add('drag-over');
        });

        folderHeader.addEventListener('dragleave', () => {
            folderHeader.classList.remove('drag-over');
        });

        folderHeader.addEventListener('drop', (e) => {
            e.preventDefault();
            folderHeader.classList.remove('drag-over');
            const draggedId = e.dataTransfer.getData('text/plain');
            moveFileToFolder(draggedId, file.id);
        });

        // 添加点击展开/折叠功能
        folderHeader.addEventListener('click', (e) => {
            if (e.target === folderHeader) {
                toggleFolder(file.id);
            }
        });

        // 添加右键菜单
        folderHeader.addEventListener('contextmenu', (e) => {
            e.preventDefault();
            const menu = document.getElementById('folderContextMenu');
            menu.style.display = 'block';
            menu.style.left = e.pageX + 'px';
            menu.style.top = e.pageY + 'px';
            menu.setAttribute('data-folder-id', file.id);
        });

        const folderContent = document.createElement('div');
        folderContent.className = 'folder-content';
        folderContent.setAttribute('data-folder-content', file.id);

        // 递归创建子文件和文件夹
        if (file.files) {
            file.files.forEach(childFile => {
                const childElement = createFileElement(childFile);
                folderContent.appendChild(childElement);
            });
        }

        folderDiv.appendChild(folderHeader);
        folderDiv.appendChild(folderContent);
        li.appendChild(folderDiv);
    } else {
        // 创建文件链接
        const a = document.createElement('a');
        a.href = '#';
        a.textContent = file.name;
        a.setAttribute('data-file', file.name);
        a.setAttribute('data-file-id', file.id);

        // 添加拖拽功能
        a.draggable = true;
        a.addEventListener('dragstart', (e) => {
            e.dataTransfer.setData('text/plain', file.id);
            e.dataTransfer.effectAllowed = 'move';
        });

        // 添加文件相关的事件监听器
        a.addEventListener('click', (e) => {
            e.preventDefault();
            const fileContent = localStorage.getItem(`file_${file.id}`);
            if (fileContent) {
                x_editor.setValue(fileContent);
            }

            document.querySelectorAll('#fileListItems a').forEach(link => {
                link.classList.remove('selected');
            });
            e.target.classList.add('selected');
        });

        a.addEventListener('contextmenu', (e) => {
            e.preventDefault();
            const menu = document.getElementById('fileContextMenu');
            menu.style.display = 'block';
            menu.style.left = e.pageX + 'px';
            menu.style.top = e.pageY + 'px';
            menu.setAttribute('data-target-file-id', file.id);
        });

        li.appendChild(a);
    }

    return li;
}

function toggleFolder(folderId) {
    const content = document.querySelector(`[data-folder-content="${folderId}"]`);
    const header = document.querySelector(`[data-folder-header="${folderId}"]`);

    if (!header.hasAttribute('draggable')) {
        header.draggable = true;
        header.addEventListener('dragstart', (e) => {
            e.dataTransfer.setData('text/plain', folderId);
            e.dataTransfer.effectAllowed = 'move';
        });
    }

    content.classList.toggle('open');
    header.classList.toggle('open');
}

function addFolder() {
    // 关闭右键菜单
    document.getElementById('rootContextMenu').style.display = 'none';

    const folderName = prompt('请输入文件夹名称：', 'New Folder');
    if (!folderName) return;

    const newFolder = {
        id: generateUUID(),
        name: folderName,
        type: 'folder',
        files: []
    };

    const filesData = localStorage.getItem('controllerFiles');
    const files = filesData ? JSON.parse(filesData) : [];
    files.push(newFolder);
    localStorage.setItem('controllerFiles', JSON.stringify(files));
    loadFileList();
}

function saveCode(code) {

    try {
        const fileId = document.querySelector('#fileListItems a.selected')?.getAttribute('data-file-id');
        if (!fileId) {
            const newFileId = generateUUID();
            const fileName = prompt('请输入文件名称：', 'New File');
            if (!fileName) return;
            const newFile = {
                id: newFileId,
                name: fileName,
                type: 'file',
                content: code
            };
            const filesData = localStorage.getItem('controllerFiles');
            const files = filesData ? JSON.parse(filesData) : [];
            files.push(newFile);
            localStorage.setItem('controllerFiles', JSON.stringify(files));
            // 保存到 file localStorage
            localStorage.setItem(`file_${newFileId}`, code);
            loadFileList();

            // 选中这个新文件
            const fileListItems = document.getElementById('fileListItems');
            const newFileElement = fileListItems.querySelector(`[data-file-id="${newFileId}"]`);
            if (newFileElement) {
                newFileElement.classList.add('selected');
            }
            return;
        }

        // 保存到 localStorage
        localStorage.setItem(`file_${fileId}`, code);
        // 保存到文件列表
        const filesData = localStorage.getItem('controllerFiles');
        const files = filesData ? JSON.parse(filesData) : [];
        files.forEach(file => {
            if (file.id === fileId) {
                file.content = code;
            }
        });
        localStorage.setItem('controllerFiles', JSON.stringify(files));
        showNotification('保存成功', 'success');
    } catch (error) {
        console.error('Error saving to localStorage:', error);
        showNotification('保存失败: ' + error.message, 'error');
    }

}

// 添加全局快捷键处理
window.addEventListener('keydown', (e) => {
    if (e.altKey && e.key.toLowerCase() === 'c') {
        e.preventDefault();
        document.getElementById('outputContent').innerHTML = '';
    }
}, true);

function formatJSON(text) {
    try {
        // 尝试解析文本为 JSON
        let jsonStart = text.indexOf('{');
        let jsonEnd = text.lastIndexOf('}');
        if (jsonStart === -1 || jsonEnd === -1) {
            // 尝试查找数组
            jsonStart = text.indexOf('[');
            jsonEnd = text.lastIndexOf(']');
        }
        if (jsonStart !== -1 && jsonEnd !== -1) {
            let jsonText = text.substring(jsonStart, jsonEnd + 1);
            const obj = JSON.parse(jsonText);
            return text.substring(0, jsonStart) + JSON.stringify(obj, null, 2) + text.substring(jsonEnd + 1);
        }
        return text;
    } catch (e) {
        return text;
    }
}

function addFile() {
    // 关闭右键菜单
    document.getElementById('rootContextMenu').style.display = 'none';

    const fileName = prompt('请输入文件名：', 'NewFile.cs');
    if (!fileName) return;

    // 创建新文件
    const newFile = {
        id: generateUUID(),
        name: fileName,
        content: `using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;

class Program
{
    public static async Task Main()
    {
        Console.WriteLine("Hello, World!");
    }
}`
    };

    // 获现有文件列表
    const filesData = localStorage.getItem('controllerFiles');
    const files = filesData ? JSON.parse(filesData) : [];

    // 添加新文件
    files.push(newFile);

    // 保存文件列表
    localStorage.setItem('controllerFiles', JSON.stringify(files));

    // 保存文件内容
    localStorage.setItem(`file_${newFile.id}`, newFile.content);

    // 刷新文件列表
    loadFileList();

    // 选中并加载新文件
    x_editor.setValue(newFile.content);
    document.querySelector(`[data-file-id="${newFile.id}"]`)?.classList.add('selected');
}

document.addEventListener('click', (e) => {
    if (!e.target.closest('.context-menu') && !e.target.closest('.folder-header')) {
        document.getElementById('fileContextMenu').style.display = 'none';
        document.getElementById('folderContextMenu').style.display = 'none';
    }
});

function renameFile() {
    const menu = document.getElementById('fileContextMenu');
    const fileId = menu.getAttribute('data-target-file-id');
    menu.style.display = 'none';

    if (!fileId) return;

    // 获取当前文件列表
    const filesData = localStorage.getItem('controllerFiles');
    const files = filesData ? JSON.parse(filesData) : [];

    // 递归查找并重命名文件
    function findAndRenameFile(items) {
        for (let i = 0; i < items.length; i++) {
            if (items[i].id === fileId) {
                const newFileName = prompt('请输入新的文件名：', items[i].name);
                if (!newFileName || newFileName === items[i].name) return;

                // 更新文件名
                items[i].name = newFileName;

                // 保存更后的文件列表
                localStorage.setItem('controllerFiles', JSON.stringify(files));

                // 刷新文件列表
                loadFileList();

                // 选中重命名的文件
                setTimeout(() => {
                    document.querySelector(`[data-file-id="${fileId}"]`)?.classList.add('selected');
                }, 0);

                showNotification('重命名成功', 'success');
                return true;
            }
            if (items[i].type === 'folder' && items[i].files) {
                if (findAndRenameFile(items[i].files)) return true;
            }
        }
        return false;
    }

    findAndRenameFile(files);
}

function deleteFile() {
    const menu = document.getElementById('fileContextMenu');
    const fileId = menu.getAttribute('data-target-file-id');
    menu.style.display = 'none';

    if (!fileId) return;

    // 获取当前文件列表
    const filesData = localStorage.getItem('controllerFiles');
    const files = filesData ? JSON.parse(filesData) : [];

    // 递归查找并删除文件
    function findAndDeleteFile(items) {
        for (let i = 0; i < items.length; i++) {
            if (items[i].id === fileId) {
                // 确认删除
                if (!confirm(`确定要删除文件 "${items[i].name}" 吗？`)) return;

                // 数组中移除文件
                items.splice(i, 1);

                // 删除文件内容
                localStorage.removeItem(`file_${fileId}`);

                // 保存更新后的文件列表
                localStorage.setItem('controllerFiles', JSON.stringify(files));

                // 刷新文件列表
                loadFileList();

                // 如果还有其他文件，选中第一个
                if (files.length > 0) {
                    const firstFile = files[0];
                    const firstFileContent = localStorage.getItem(`file_${firstFile.id}`);
                    if (firstFileContent) {
                        x_editor.setValue(firstFileContent);
                    }
                    setTimeout(() => {
                        document.querySelector(`[data-file-id="${firstFile.id}"]`)?.classList.add('selected');
                    }, 0);
                } else {
                    // 如果没有文件了，清空编辑器
                    x_editor.setValue('');
                }

                showNotification('删除成功', 'success');
                return true;
            }
            if (items[i].type === 'folder' && items[i].files) {
                if (findAndDeleteFile(items[i].files)) return true;
            }
        }
        return false;
    }

    findAndDeleteFile(files);
}

function moveFileToFolder(fileId, folderId) {
    const filesData = localStorage.getItem('controllerFiles');
    if (!filesData) return;

    const files = JSON.parse(filesData);
    let itemToMove = null;
    let sourceFolder = null;

    // 递归查找要移动的项目（可以是文件或文件夹）和其源文件夹
    function findItemAndSource(items, parent = null) {
        for (let i = 0; i < items.length; i++) {
            if (items[i].id === fileId) {
                // 创建一个深拷贝，确保保留所有属性
                itemToMove = JSON.parse(JSON.stringify(items[i]));
                sourceFolder = parent;
                return true;
            }
            if (items[i].type === 'folder' && items[i].files) {
                if (findItemAndSource(items[i].files, items[i])) {
                    return true;
                }
            }
        }
        return false;
    }

    findItemAndSource(files);

    // 查找目标文件夹
    function findFolder(items) {
        for (let item of items) {
            if (item.id === folderId) {
                // 查是否试图将文件夹移动到其自身其子文件夹中
                if (itemToMove && itemToMove.type === 'folder') {
                    function isSubfolder(folder, targetId) {
                        if (folder.id === targetId) return true;
                        if (folder.files) {
                            for (let file of folder.files) {
                                if (file.type === 'folder' && isSubfolder(file, targetId)) {
                                    return true;
                                }
                            }
                        }
                        return false;
                    }

                    if (isSubfolder(itemToMove, folderId)) {
                        showNotification('不能将文件夹移动到其自身或其子文件夹中', 'error');
                        return null;
                    }
                }
                return item;
            }
            if (item.type === 'folder' && item.files) {
                const found = findFolder(item.files);
                if (found) return found;
            }
        }
        return null;
    }

    const targetFolder = findFolder(files);

    if (itemToMove && targetFolder) {
        // 如果在根目录
        if (!sourceFolder) {
            files.splice(files.findIndex(f => f.id === fileId), 1);
        } else {
            // 如果在其他文件夹中
            sourceFolder.files = sourceFolder.files.filter(f => f.id !== fileId);
        }

        // 确保标文件夹有 files 数组
        if (!targetFolder.files) targetFolder.files = [];

        // 添加到目标文件夹，保持原有的所有属性（包括 type 和 files）
        targetFolder.files.push(itemToMove);

        // 保存更新后的文列表
        localStorage.setItem('controllerFiles', JSON.stringify(files));

        // 刷新文件列表
        loadFileList();

        showNotification(`已将 "${itemToMove.name}" 移动到 "${targetFolder.name}"`, 'success');
    }
}

function renameFolder() {
    const menu = document.getElementById('folderContextMenu');
    const folderId = menu.getAttribute('data-folder-id');
    menu.style.display = 'none';

    if (!folderId) return;

    const filesData = localStorage.getItem('controllerFiles');
    const files = filesData ? JSON.parse(filesData) : [];

    // 递归查找文件夹
    function findAndRenameFolder(items) {
        for (let item of items) {
            if (item.id === folderId) {
                const newFolderName = prompt('请输入新的文件夹名称', item.name);
                if (!newFolderName || newFolderName === item.name) return;

                item.name = newFolderName;
                localStorage.setItem('controllerFiles', JSON.stringify(files));
                loadFileList();
                showNotification('重命名功', 'success');
                return true;
            }
            if (item.type === 'folder' && item.files) {
                if (findAndRenameFolder(item.files)) return true;
            }
        }
        return false;
    }

    findAndRenameFolder(files);
}

function deleteFolder() {
    const menu = document.getElementById('folderContextMenu');
    const folderId = menu.getAttribute('data-folder-id');
    menu.style.display = 'none';

    if (!folderId) return;

    const filesData = localStorage.getItem('controllerFiles');
    const files = filesData ? JSON.parse(filesData) : [];

    // 递归查找并删除文件夹
    function findAndDeleteFolder(items) {
        for (let i = 0; i < items.length; i++) {
            if (items[i].id === folderId) {
                if (!confirm(`确定要删除文件夹 "${items[i].name}" 及其中的所有文件？`)) return;

                // 递归删除文件夹中的所有文件内容
                function deleteFilesRecursively(folder) {
                    if (folder.files) {
                        folder.files.forEach(file => {
                            if (file.type === 'folder') {
                                deleteFilesRecursively(file);
                            } else {
                                localStorage.removeItem(`file_${file.id}`);
                            }
                        });
                    }
                }

                deleteFilesRecursively(items[i]);

                // 从数组中移除文件夹
                items.splice(i, 1);
                localStorage.setItem('controllerFiles', JSON.stringify(files));
                loadFileList();
                showNotification('删除成功', 'success');
                return true;
            }
            if (items[i].type === 'folder' && items[i].files) {
                if (findAndDeleteFolder(items[i].files)) return true;
            }
        }
        return false;
    }

    findAndDeleteFolder(files);
}

// 添加侧边栏调整大小的功能
function initializeFileListResize() {
    const fileList = document.getElementById('fileList');
    const container = document.getElementById('container');

    let isResizing = false;
    let startX;
    let startWidth;

    fileList.addEventListener('mousedown', (e) => {
        // 只在右边框附近 10px 范围内触发
        const rightEdge = fileList.getBoundingClientRect().right;
        if (Math.abs(e.clientX - rightEdge) > 10) return;

        isResizing = true;
        startX = e.clientX;
        startWidth = parseInt(document.defaultView.getComputedStyle(fileList).width, 10);
        fileList.classList.add('resizing');
        document.documentElement.style.cursor = 'ew-resize';
        e.preventDefault();  // 防止文本选择
    });

    document.addEventListener('mousemove', (e) => {
        if (!isResizing) return;

        const width = startWidth + (e.clientX - startX);
        if (width >= 300 && width <= 800) {
            fileList.style.width = `${width}px`;
            container.style.marginLeft = `${width}px`;
            container.style.width = `calc(100% - ${width}px)`;
            if (window.x_editor) {
                window.x_editor.layout();
            }
        }
    });

    document.addEventListener('mouseup', () => {
        if (isResizing) {
            isResizing = false;
            fileList.classList.remove('resizing');
            document.documentElement.style.cursor = '';
        }
    });

    // 当鼠标移动到右边框附近时改变光标
    fileList.addEventListener('mousemove', (e) => {
        const rightEdge = fileList.getBoundingClientRect().right;
        if (Math.abs(e.clientX - rightEdge) <= 10) {
            fileList.style.cursor = 'ew-resize';
        } else {
            fileList.style.cursor = 'default';
        }
    });

    fileList.addEventListener('mouseleave', () => {
        if (!isResizing) {
            fileList.style.cursor = 'default';
        }
    });
}

// 在页面加载时初始化整大小
window.addEventListener('load', () => {
    initializeFileListResize();
});

function reorderFiles(draggedId, targetId, position) {
    const filesData = localStorage.getItem('controllerFiles');
    if (!filesData) return;

    const files = JSON.parse(filesData);

    // 递归查找并移除拖拽的项目
    function findAndRemoveItem(items) {
        for (let i = 0; i < items.length; i++) {
            if (items[i].id === draggedId) {
                return items.splice(i, 1)[0];
            }
            if (items[i].type === 'folder' && items[i].files) {
                const found = findAndRemoveItem(items[i].files);
                if (found) return found;
            }
        }
        return null;
    }

    // 递归查找目标位置并插入项目
    function findAndInsertItem(items, draggedItem) {
        for (let i = 0; i < items.length; i++) {
            if (items[i].id === targetId) {
                const insertIndex = position === 'before' ? i : i + 1;
                items.splice(insertIndex, 0, draggedItem);
                return true;
            }
            if (items[i].type === 'folder' && items[i].files) {
                if (findAndInsertItem(items[i].files, draggedItem)) return true;
            }
        }
        return false;
    }

    const draggedItem = findAndRemoveItem(files);
    if (draggedItem) {
        if (!findAndInsertItem(files, draggedItem)) {
            // 果没找到目标位置，项目添加到末尾
            files.push(draggedItem);
        }
        localStorage.setItem('controllerFiles', JSON.stringify(files));
        const expandedFolders = saveExpandedFolders();
        loadFileList();
        restoreExpandedFolders(expandedFolders);
    }
}

function moveItem(itemId, direction) {
    const filesData = localStorage.getItem('controllerFiles');
    if (!filesData) return;

    const files = JSON.parse(filesData);

    // 递归查找并移动项目
    function findAndMoveItem(items) {
        for (let i = 0; i < items.length; i++) {
            if (items[i].id === itemId) {
                if (direction === 'up' && i > 0) {
                    // 上移
                    [items[i - 1], items[i]] = [items[i], items[i - 1]];
                    return true;
                } else if (direction === 'down' && i < items.length - 1) {
                    // 下移
                    [items[i], items[i + 1]] = [items[i + 1], items[i]];
                    return true;
                }
                return false;
            }
            if (items[i].type === 'folder' && items[i].files) {
                if (findAndMoveItem(items[i].files)) return true;
            }
        }
        return false;
    }

    if (findAndMoveItem(files)) {
        localStorage.setItem('controllerFiles', JSON.stringify(files));
        const expandedFolders = saveExpandedFolders();
        loadFileList();
        restoreExpandedFolders(expandedFolders);
    }
}

// 添加在文件夹中新建文件的功能
function addFileToFolder(folderId) {
    // 关闭右键菜单
    document.getElementById('folderContextMenu').style.display = 'none';
    document.getElementById('rootContextMenu').style.display = 'none';

    const fileName = prompt('请输入文件名：', 'New File.cs');
    if (!fileName) return;

    const newFile = {
        id: generateUUID(),
        name: fileName,
        content: `using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;

class Program
{
    public static async Task Main()
    {
        Console.WriteLine("Hello, World!");
    }
}`
    };

    const filesData = localStorage.getItem('controllerFiles');
    const files = filesData ? JSON.parse(filesData) : [];

    // 递归查找目标文件夹
    function findFolder(items) {
        for (let item of items) {
            if (item.id === folderId) {
                if (!item.files) item.files = [];
                item.files.push(newFile);
                return true;
            }
            if (item.type === 'folder' && item.files) {
                if (findFolder(item.files)) return true;
            }
        }
        return false;
    }

    findFolder(files);

    // 保存文件列表和文件内容
    localStorage.setItem('controllerFiles', JSON.stringify(files));
    localStorage.setItem(`file_${newFile.id}`, newFile.content);

    // 保存当前展开的文件夹，并添加目标文件夹
    const expandedFolders = saveExpandedFolders();
    if (!expandedFolders.includes(folderId)) {
        expandedFolders.push(folderId);
    }

    // 刷新文件列表并恢复展开状态
    loadFileList();
    restoreExpandedFolders(expandedFolders);

    // 选中新建的文件
    setTimeout(() => {
        const newFileElement = document.querySelector(`[data-file-id="${newFile.id}"]`);
        if (newFileElement) {
            newFileElement.classList.add('selected');
            const fileContent = localStorage.getItem(`file_${newFile.id}`);
            if (fileContent) {
                x_editor.setValue(fileContent);
            }
        }
    }, 0);
}

// 添加在文件夹中新建文件夹的功能
function addFolderToFolder(parentFolderId) {
    // 关闭右键菜单
    document.getElementById('folderContextMenu').style.display = 'none';
    document.getElementById('rootContextMenu').style.display = 'none';

    const folderName = prompt('请输入文件夹名称：', 'New Folder');
    if (!folderName) return;

    const newFolder = {
        id: generateUUID(),
        name: folderName,
        type: 'folder',
        files: []
    };

    const filesData = localStorage.getItem('controllerFiles');
    const files = filesData ? JSON.parse(filesData) : [];

    // 递归查找目标文件夹
    function findFolder(items) {
        for (let item of items) {
            if (item.id === parentFolderId) {
                if (!item.files) item.files = [];
                item.files.push(newFolder);
                return true;
            }
            if (item.type === 'folder' && item.files) {
                if (findFolder(item.files)) return true;
            }
        }
        return false;
    }

    findFolder(files);

    // 保存文件列表
    localStorage.setItem('controllerFiles', JSON.stringify(files));

    // 保存当前展开的文件夹，并添加父文件夹和新文件夹
    const expandedFolders = saveExpandedFolders();
    if (!expandedFolders.includes(parentFolderId)) {
        expandedFolders.push(parentFolderId);
    }
    expandedFolders.push(newFolder.id);

    // 刷新文件列表并恢复展开状态
    loadFileList();
    restoreExpandedFolders(expandedFolders);
}

// 显示根目录的右键菜单
function showRootContextMenu(event) {
    // 检查是否点击在空白区域
    if (event.target === document.getElementById('fileListItems')) {
        event.preventDefault();
        const menu = document.getElementById('rootContextMenu');
        menu.style.display = 'block';
        menu.style.left = event.pageX + 'px';
        menu.style.top = event.pageY + 'px';
    }
}

// 点击其他地方时隐藏所有右键菜单
document.addEventListener('click', (e) => {
    if (!e.target.closest('.context-menu')) {
        document.getElementById('fileContextMenu').style.display = 'none';
        document.getElementById('folderContextMenu').style.display = 'none';
        document.getElementById('rootContextMenu').style.display = 'none';
    }
});

// NuGet Configuration Functions
let currentFileId = null;

window.configureNuGet = function configureNuGet() {
    const fileId = document.getElementById('fileContextMenu').getAttribute('data-target-file-id');
    currentFileId = fileId;

    // 从 localStorage 获取文件列表
    const filesData = localStorage.getItem('controllerFiles');
    const files = filesData ? JSON.parse(filesData) : [];

    // 递归查找目标文件
    function findFile(items) {
        for (let item of items) {
            if (item.id === fileId) {
                return item;
            }
            if (item.type === 'folder' && item.files) {
                const found = findFile(item.files);
                if (found) return found;
            }
        }
        return null;
    }

    const file = findFile(files);
    if (!file) return;

    // Show dialog
    const dialog = document.getElementById('nugetConfigDialog');
    dialog.style.display = 'block';
    loadNuGetConfig(file);
}

function closeNuGetConfigDialog() {
    const dialog = document.getElementById('nugetConfigDialog');
    dialog.style.display = 'none';
    currentFileId = null;
}

async function loadNuGetConfig(file) {
    try {
        // Load package references
        const packagesDiv = document.getElementById('packageReferences');
        const config = file.nugetConfig || {
            packages: []
        };

        // Display packages
        packagesDiv.innerHTML = config.packages.length === 0 ?
            '<div class="no-packages-message">暂无包引用</div>' :
            config.packages.map(pkg => `
                <div class="package-reference">
                    <div class="package-reference-info">
                        <span class="package-reference-name">${pkg.id}</span>
                        <span class="package-reference-version">${pkg.version}</span>
                    </div>
                    <button class="remove-button" onclick="removePackageReference('${pkg.id}')">移除</button>
                </div>
            `).join('');

    } catch (error) {
        console.error('Load NuGet config error:', error);
        showNotification('加载 NuGet 配置失败', true);
    }
}

async function saveNuGetConfig() {
    try {
        const file = files.find(f => f.id === currentFileId);
        if (!file) return;

        // Get form values
        const config = {
            targetFramework: document.getElementById('targetFramework').value,
            packageSource: document.getElementById('packageSource').value,
            packages: file.nugetConfig?.packages || []
        };

        // Save configuration
        file.nugetConfig = config;

        // Save to localStorage
        localStorage.setItem('files', JSON.stringify(files));

        showNotification('NuGet 配置保存成功');
        closeNuGetConfigDialog();

    } catch (error) {
        console.error('Save NuGet config error:', error);
        showNotification('保存 NuGet 配置失败', true);
    }
}

function removePackageReference(packageId) {
    try {
        const file = getCurrentFile();
        if (!file || !file.nugetConfig) return;

        // 移除包引用
        file.nugetConfig.packages = file.nugetConfig.packages.filter(p => p.id !== packageId);

        const files = GetCurrentFiles();

        // 递归更新文件列表中的文件
        function updateFile(items) {
            for (let item of items) {
                if (item.id === file.id) {
                    item.nugetConfig = file.nugetConfig;
                    return true;
                }
                if (item.type === 'folder' && item.files) {
                    if (updateFile(item.files)) return true;
                }
            }
            return false;
        }

        updateFile(files);

        // 保存到 localStorage
        localStorage.setItem('controllerFiles', JSON.stringify(files));

        // 重新加载配置
        loadNuGetConfig(file);

        showNotification(`包引用 ${packageId} 已移除`, 'success');
    } catch (error) {
        console.error('Remove package reference error:', error);
        showNotification(`移除包引用 ${packageId} 失败: ${error.message}`, 'error');
    }
}

// Initialize NuGet dialog event listeners
document.addEventListener('DOMContentLoaded', () => {
    // Close dialog when clicking outside
    document.getElementById('nugetConfigDialog')?.addEventListener('click', (e) => {
        if (e.target === document.getElementById('nugetConfigDialog')) {
            closeNuGetConfigDialog();
        }
    });
});

// Add this function to handle adding new packages
async function addPackageReference() {
    // 从 localStorage 获取文件列表
    const file = getCurrentFile();
    if (!file) return;

    // Initialize nugetConfig if it doesn't exist
    if (!file.nugetConfig) {
        file.nugetConfig = {
            packages: []
        };
    }

    // 创建并显示自定义对话框
    const dialog = document.createElement('div');
    dialog.id = 'packageDialog';
    dialog.className = 'modal';
    dialog.style.display = 'block';
    dialog.style.zIndex = '9999';

    // 获取NuGet配置窗口的位置
    const nugetDialog = document.getElementById('nugetConfigDialog');
    const nugetRect = nugetDialog.getBoundingClientRect();

    dialog.innerHTML = `
        <div class="modal-content" style="position: fixed; top: ${nugetRect.top - 20}px; left: 50%; transform: translateX(-50%); width: 400px; background: #2d2d2d; border-radius: 8px; box-shadow: 0 4px 12px rgba(0,0,0,0.3);">
            <div class="modal-header" style="padding: 16px; border-bottom: 1px solid #404040;">
                <h2 style="margin: 0; color: #e0e0e0; font-size: 18px;">添加 NuGet 包引用</h2>
            </div>
            <div class="modal-body" style="padding: 20px;">
                <div class="nuget-config-section">
                    <div class="form-group" style="margin-bottom: 16px;">
                        <label for="packageName" style="display: block; margin-bottom: 8px; color: #e0e0e0;">包名:</label>
                        <input type="text" id="packageName" class="form-control" placeholder="例如：Newtonsoft.Json" 
                            style="width: 100%; padding: 8px; background: #3d3d3d; border: 1px solid #505050; border-radius: 4px; color: #e0e0e0;">
                    </div>
                    <div class="form-group" style="margin-bottom: 20px;">
                        <label for="packageVersion" style="display: block; margin-bottom: 8px; color: #e0e0e0;">版本:</label>
                        <input type="text" id="packageVersion" class="form-control" placeholder="例如：13.0.1"
                            style="width: 100%; padding: 8px; background: #3d3d3d; border: 1px solid #505050; border-radius: 4px; color: #e0e0e0;">
                    </div>
                    <div class="button-group" style="display: flex; justify-content: flex-end; gap: 10px;">
                        <button id="cancelButton" class="secondary-button" 
                            style="padding: 8px 16px; border-radius: 4px; border: 1px solid #505050; background: #3d3d3d; color: #e0e0e0; cursor: pointer;">取消</button>
                        <button id="confirmButton" class="primary-button"
                            style="padding: 8px 16px; border-radius: 4px; border: none; background: #0078d4; color: white; cursor: pointer;">确认</button>
                    </div>
                </div>
            </div>
        </div>
    `;

    document.body.appendChild(dialog);

    // 返回一个 Promise 来处理用户输入
    const getUserInput = () => {
        return new Promise((resolve, reject) => {
            const confirmButton = document.getElementById('confirmButton');
            const cancelButton = document.getElementById('cancelButton');
            const packageNameInput = document.getElementById('packageName');
            const packageVersionInput = document.getElementById('packageVersion');

            confirmButton.addEventListener('click', () => {
                const name = packageNameInput.value.trim();
                const version = packageVersionInput.value.trim();
                if (!name || !version) {
                    showNotification('包名和版��不能为空', true);
                    return;
                }
                resolve({ name, version });
                document.body.removeChild(dialog);
            });

            cancelButton.addEventListener('click', () => {
                reject('用户取消了操作');
                document.body.removeChild(dialog);
            });
        });
    };

    let userInput;
    try {
        userInput = await getUserInput();
    } catch (error) {
        // 用户取消了操作
        return;
    }

    const { name, version } = userInput;

    // 检查是否已存在相同的包
    if (file.nugetConfig.packages.some(p => p.id.toLowerCase() === name.toLowerCase())) {
        showNotification(`包 ${name} 已存在`, true);
        return;
    }

    // 调用后端添加包引用
    const request = {
        Packages: [{ Id: name, Version: version }]
    };

    try {
        const result = await sendRequest('addPackages', request);
        if (result.data.code === 0) {
            showNotification(`已添加包引用: ${name}@${version}`, 'success');
        } else {
            showNotification(`添加包引用失败: ${result.data.message}`, 'error');
            return;
        }
    } catch (err) {
        console.error('添加包引用错误:', err);
        showNotification(`添加���引用失败: ${err.message}`, 'error');
        return;
    }

    // 更新文件的 nugetConfig
    file.nugetConfig.packages.push({ id: name, version: version });

    // 从 localStorage 获取最新的文件列表
    const files = GetCurrentFiles();

    // 递归更新文件列表中的文件
    function updateFile(items) {
        for (let item of items) {
            if (item.id === file.id) {
                item.nugetConfig = file.nugetConfig;
                return true;
            }
            if (item.type === 'folder' && item.files) {
                if (updateFile(item.files)) return true;
            }
        }
        return false;
    }

    updateFile(files);

    // 保存更新后的文件列表
    localStorage.setItem('controllerFiles', JSON.stringify(files));

    // 重新加载 NuGet 配置显示
    loadNuGetConfig(file);
}

function GetCurrentFiles() {
    const filesData = localStorage.getItem('controllerFiles');
    const files = filesData ? JSON.parse(filesData) : [];
    return files;
}

function getCurrentFile() {
    // 获取当前选中的文件
    const selectedFile = document.querySelector('.selected[data-file-id]');
    if (!selectedFile) return null;

    // 获取文件ID
    const fileId = selectedFile.getAttribute('data-file-id');
    currentFileId = fileId;

    // 从 localStorage 获取文件列表
    const filesData = localStorage.getItem('controllerFiles');
    const files = filesData ? JSON.parse(filesData) : [];

    // 递归查找目标文件
    function findFile(items) {
        for (let item of items) {
            if (item.id === fileId) {
                return item;
            }
            if (item.type === 'folder' && item.files) {
                const found = findFile(item.files);
                if (found) return found;
            }
        }
        return null;
    }

    return findFile(files);
}

function generateUUID() {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
        var r = Math.random() * 16 | 0,
            v = c == 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
    });
}

// 递归查找文件
function findFile(items, fileId) {
    for (let item of items) {
        if (item.id === fileId) {
            return item;
        }
        if (item.type === 'folder' && item.files) {
            const found = findFile(item.files, fileId);
            if (found) return found;
        }
    }
    return null;
}

// 查找原文件的父文件夹
function findParentFolder(items, fileId) {
    for (let item of items) {
        if (item.type === 'folder' && item.files) {
            if (item.files.some(file => file.id === fileId)) {
                return item;
            }
            const found = findParentFolder(item.files, fileId);
            if (found) return found;
        }
    }
    return null;
}

function duplicateFile() {
    const fileId = document.getElementById('fileContextMenu').getAttribute('data-target-file-id');
    const files = GetCurrentFiles();

    const originalFile = findFile(files, fileId);

    if (!originalFile) return;

    // 创建新文件对象
    const newFile = {
        id: generateUUID(),
        name: `${originalFile.name} (副本)`,
        content: originalFile.content,
        nugetConfig: originalFile.nugetConfig || { packages: [] }
    };

    const parentFolder = findParentFolder(files, originalFile.id);
    if (parentFolder) {
        parentFolder.files.push(newFile);
    } else {
        files.push(newFile);
    }

    // 保存文件内容
    localStorage.setItem(`file_${newFile.id}`, newFile.content);

    // 保存文件列表
    localStorage.setItem('controllerFiles', JSON.stringify(files));

    // 刷新文件列表显示
    loadFileList();

    // 关闭上下文菜单
    const contextMenus = document.querySelectorAll('.context-menu');
    contextMenus.forEach(menu => {
        menu.style.display = 'none';
    });

    // 显示通知
    showNotification('文件已复制', 'success');
}

function duplicateFolder() {
    const menu = document.getElementById('folderContextMenu');
    const folderId = menu.getAttribute('data-folder-id');
    menu.style.display = 'none';

    if (!folderId) return;

    const filesData = localStorage.getItem('controllerFiles');
    const files = filesData ? JSON.parse(filesData) : [];

    // 递归查找并复制文件夹
    function findAndDuplicateFolder(items) {
        for (let item of items) {
            if (item.id === folderId) {
                // 深度复制文件夹及其内容
                const duplicatedFolder = JSON.parse(JSON.stringify(item));

                // 为复制的文件夹及其所有子文件生成新的ID
                function generateNewIds(folder) {
                    folder.id = generateUUID();
                    if (folder.files) {
                        folder.files.forEach(file => {
                            if (file.type === 'folder') {
                                generateNewIds(file);
                            } else {
                                const oldId = file.id;
                                file.id = generateUUID();
                                // 复制文件内容
                                const fileContent = localStorage.getItem(`file_${oldId}`);
                                if (fileContent) {
                                    localStorage.setItem(`file_${file.id}`, fileContent);
                                }
                            }
                        });
                    }
                }

                generateNewIds(duplicatedFolder);
                duplicatedFolder.name = `${item.name} (副本)`;

                // 将复制的文件夹添加到同级目录
                const parentArray = items;
                const index = parentArray.findIndex(i => i.id === folderId);
                parentArray.splice(index + 1, 0, duplicatedFolder);

                // 保存更新后的文件列表
                localStorage.setItem('controllerFiles', JSON.stringify(files));

                // 刷新文件列表并展开复制的文件夹
                const expandedFolders = saveExpandedFolders();
                expandedFolders.push(duplicatedFolder.id);
                loadFileList();
                restoreExpandedFolders(expandedFolders);

                showNotification('文件夹已复制', 'success');
                return true;
            }
            if (item.type === 'folder' && item.files) {
                if (findAndDuplicateFolder(item.files)) return true;
            }
        }
        return false;
    }

    findAndDuplicateFolder(files);
}

function exportFolder() {
    const menu = document.getElementById('folderContextMenu');
    const folderId = menu.getAttribute('data-folder-id');
    menu.style.display = 'none';

    if (!folderId) return;

    const filesData = localStorage.getItem('controllerFiles');
    const files = filesData ? JSON.parse(filesData) : [];

    // 递归查找文件夹
    function findFolder(items) {
        for (let item of items) {
            if (item.id === folderId) {
                // 创建一个包含文件夹内容的对象
                const folderData = {
                    name: item.name,
                    type: 'folder',
                    files: []
                };

                // 递归获取文件夹中的所有文件内容
                function getFilesContent(folder, targetArray) {
                    if (folder.files) {
                        folder.files.forEach(file => {
                            if (file.type === 'folder') {
                                const subFolder = {
                                    name: file.name,
                                    type: 'folder',
                                    files: []
                                };
                                targetArray.push(subFolder);
                                getFilesContent(file, subFolder.files);
                            } else {
                                const fileContent = localStorage.getItem(`file_${file.id}`);
                                targetArray.push({
                                    name: file.name,
                                    content: fileContent,
                                    nugetConfig: file.nugetConfig
                                });
                            }
                        });
                    }
                }

                getFilesContent(item, folderData.files);

                // 创建并下载 JSON 文件
                const jsonContent = JSON.stringify(folderData, null, 2);
                const blob = new Blob([jsonContent], { type: 'application/json' });
                const url = URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = `${item.name}.json`;
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                URL.revokeObjectURL(url);

                showNotification('文件夹已导出', 'success');
                return true;
            }
            if (item.type === 'folder' && item.files) {
                if (findFolder(item.files)) return true;
            }
        }
        return false;
    }

    findFolder(files);
}

function importFolder() {
    const menu = document.getElementById('folderContextMenu');
    const targetFolderId = menu.getAttribute('data-folder-id');
    menu.style.display = 'none';

    if (!targetFolderId) return;

    // 创建文件输入元素
    const fileInput = document.createElement('input');
    fileInput.type = 'file';
    fileInput.accept = '.json';
    fileInput.style.display = 'none';
    document.body.appendChild(fileInput);

    fileInput.onchange = function (e) {
        const file = e.target.files[0];
        if (!file) {
            document.body.removeChild(fileInput);
            return;
        }

        const reader = new FileReader();
        reader.onload = function (e) {
            try {
                const importedData = JSON.parse(e.target.result);

                // 验证导入的数据格式
                if (!importedData.name || !importedData.type || !Array.isArray(importedData.files)) {
                    throw new Error('无效的文件格式');
                }

                const filesData = localStorage.getItem('controllerFiles');
                const files = filesData ? JSON.parse(filesData) : [];

                // 递归生成新的 ID
                function regenerateIds(items) {
                    return items.map(item => {
                        const newItem = { ...item, id: generateUUID() };
                        if (item.type === 'folder' && Array.isArray(item.files)) {
                            newItem.files = regenerateIds(item.files);
                        }
                        return newItem;
                    });
                }

                // 递归保存文件内容
                function saveFileContents(items) {
                    items.forEach(item => {
                        if (item.type !== 'folder' && item.content) {
                            localStorage.setItem(`file_${item.id}`, item.content);
                        }
                        if (item.type === 'folder' && Array.isArray(item.files)) {
                            saveFileContents(item.files);
                        }
                    });
                }

                // 查找目标文件夹并添加导入的内容
                function findAndAddToFolder(items) {
                    for (let item of items) {
                        if (item.id === targetFolderId) {
                            if (!item.files) {
                                item.files = [];
                            }
                            // 生成新的 ID 并保存文件内容
                            const importedFiles = regenerateIds(importedData.files);
                            saveFileContents(importedFiles);
                            item.files.push(...importedFiles);
                            return true;
                        }
                        if (item.type === 'folder' && item.files) {
                            if (findAndAddToFolder(item.files)) return true;
                        }
                    }
                    return false;
                }

                if (findAndAddToFolder(files)) {
                    localStorage.setItem('controllerFiles', JSON.stringify(files));
                    loadFileList();
                    showNotification('导入成功', 'success');
                }

            } catch (error) {
                console.error('导入错误:', error);
                showNotification('导入失败: ' + error.message, 'error');
            }
            document.body.removeChild(fileInput);
        };

        reader.onerror = function () {
            showNotification('读取文件失败', 'error');
            document.body.removeChild(fileInput);
        };

        reader.readAsText(file);
    };

    fileInput.click();
}

function moveOutOfFolder() {
    // 获取要移动的文件ID
    const menu = document.getElementById('fileContextMenu');
    const fileId = menu.getAttribute('data-target-file-id');
    menu.style.display = 'none';

    // 获取存储的文件列表
    let files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');

    // 递归查找文件和其父文件夹
    function findFileAndParentFolder(items, parentFolder = null) {
        for (let i = 0; i < items.length; i++) {
            const item = items[i];
            if (item.id === fileId) {
                return { file: item, parent: parentFolder, parentArray: items, index: i };
            }
            if (item.type === 'folder' && Array.isArray(item.files)) {
                const result = findFileAndParentFolder(item.files, item);
                if (result) return result;
            }
        }
        return null;
    }

    // 查找文件和父文件夹
    const result = findFileAndParentFolder(files);
    if (result) {
        // 从原文件夹中移除
        result.parentArray.splice(result.index, 1);
        // 添加到根目录
        files.push(result.file);
        // 保存更新后的文件列表
        localStorage.setItem('controllerFiles', JSON.stringify(files));
        // 重新加载文件列表
        loadFileList();
        showNotification('文件已移出文件夹', 'success');
    }
}



