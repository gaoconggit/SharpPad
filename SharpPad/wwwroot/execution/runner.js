import { getCurrentFile } from '../utils/common.js';
import { sendRequest } from '../utils/apiService.js';

export class CodeRunner {
    constructor() {
        this.runButton = document.getElementById('runButton');
        this.outputContent = document.getElementById('outputContent');
        this.notification = document.getElementById('notification');
        this.initializeEventListeners();
    }

    initializeEventListeners() {
        this.runButton.addEventListener('click', () => this.runCode());
    }

    async runCode() {
        if (!window.editor) return;

        const sourceCode = window.editor.getValue();
        if (!sourceCode.trim()) return;

        // 清空输出区域
        this.outputContent.innerHTML = '';
        this.outputContent.className = 'running';

        const file = getCurrentFile();
        const packages = file?.nugetConfig?.packages || [];

        const request = {
            SourceCode: sourceCode,
            Packages: packages.map(p => ({
                Id: p.id,
                Version: p.version
            }))
        };

        try {
            let result = "";
            const { reader, showNotificationTimer } = await sendRequest('run', request);

            while (true) {
                const { value, done } = await reader.read();
                if (done) break;

                const text = new TextDecoder().decode(value);
                const lines = text.split('\n');

                for (const line of lines) {
                    if (!line.trim()) continue;
                    if (!line.startsWith('data: ')) continue;

                    const data = JSON.parse(line.substring(6));
                    switch (data.type) {
                        case 'output':
                            result += data.content;
                            this.streamOutput(result, 'success');
                            break;
                        case 'error':
                            this.appendOutput(data.content, 'error');
                            this.notification.textContent = '运行出错';
                            this.notification.style.backgroundColor = 'rgba(244, 67, 54, 0.9)';
                            this.notification.style.display = 'block';
                            break;
                        case 'completed':
                            clearTimeout(showNotificationTimer);
                            this.notification.style.display = 'none';
                            this.outputContent.classList.remove("result-streaming");
                            return;
                    }
                }
            }
        } catch (error) {
            this.appendOutput('运行请求失败: ' + error.message, 'error');
            this.notification.textContent = '运行失败';
            this.notification.style.backgroundColor = 'rgba(244, 67, 54, 0.9)';
            this.notification.style.display = 'block';
        }
    }

    streamOutput(content, type) {
        this.outputContent.innerHTML = '';
        this.appendOutput(content, type);
    }

    appendOutput(content, type) {
        const div = document.createElement('div');
        div.className = `output-line ${type}`;
        div.textContent = content;
        this.outputContent.appendChild(div);
        this.outputContent.scrollTop = this.outputContent.scrollHeight;
    }
} 