const listeners = new Set();
const readyCallbacks = [];

let isReady = false;
let platformInfo = null;

function dispatchIncomingMessage(rawMessage) {
    let payload = rawMessage;

    if (typeof rawMessage === 'string') {
        try {
            payload = JSON.parse(rawMessage);
        } catch (error) {
            console.warn('Desktop bridge: 无法解析宿主消息', error);
            return;
        }
    }

    if (payload?.type === 'host-ready') {
        isReady = true;
        platformInfo = payload.platform ?? null;
        while (readyCallbacks.length > 0) {
            const callback = readyCallbacks.shift();
            try {
                callback(payload);
            } catch (error) {
                console.error('Desktop bridge: ready 回调执行失败', error);
            }
        }
    }

    for (const callback of listeners) {
        try {
            callback(payload);
        } catch (error) {
            console.error('Desktop bridge: 回调执行失败', error);
        }
    }
}

const chromeWebView = window.chrome?.webview;
const webkitHandler = window.webkit?.messageHandlers?.webview;

if (chromeWebView) {
    chromeWebView.addEventListener('message', event => dispatchIncomingMessage(event.data));
} else if (webkitHandler) {
    window.__dispatchMessageCallback = message => dispatchIncomingMessage(message);
}

function flushPendingMessages() {
    const pending = window.__avaloniaPendingMessages;
    if (Array.isArray(pending) && pending.length > 0) {
        const queue = pending.splice(0, pending.length);
        for (const message of queue) {
            dispatchIncomingMessage(message);
        }
    }
}

function sendToHost(message) {
    const serialized = typeof message === 'string' ? message : JSON.stringify(message);

    if (chromeWebView) {
        chromeWebView.postMessage(serialized);
        return true;
    }

    if (webkitHandler) {
        webkitHandler.postMessage(serialized);
        return true;
    }

    return false;
}

const desktopBridge = {
    isAvailable: Boolean(chromeWebView || webkitHandler),
    isReady: () => isReady,
    platform: () => platformInfo,
    send: sendToHost,
    onMessage(callback) {
        listeners.add(callback);
        return () => listeners.delete(callback);
    },
    onceHostReady(callback) {
        if (isReady) {
            callback({ type: 'host-ready', platform: platformInfo });
        } else {
            readyCallbacks.push(callback);
        }
        return () => {
            const index = readyCallbacks.indexOf(callback);
            if (index >= 0) {
                readyCallbacks.splice(index, 1);
            }
        };
    },
    requestPickAndUpload(endpoint, context) {
        const payload = {
            type: 'pick-and-upload'
        };

        if (typeof endpoint === 'string' && endpoint.trim().length > 0) {
            payload.endpoint = endpoint.trim();
        }

        if (typeof context !== 'undefined') {
            payload.context = context;
        }

        const posted = sendToHost(payload);
        if (!posted) {
            console.warn('Desktop bridge: 当前环境不支持宿主消息通道。');
        }
    },
    requestFileDownload(options) {
        const payload = {
            type: 'download-file'
        };

        if (options && typeof options === 'object') {
            const {
                fileName,
                content,
                mimeType,
                isBase64,
                context
            } = options;

            if (typeof fileName === 'string' && fileName.trim().length > 0) {
                payload.fileName = fileName.trim();
            }

            if (typeof content === 'string' && content.length > 0) {
                payload.content = content;
            }

            if (typeof mimeType === 'string' && mimeType.trim().length > 0) {
                payload.mimeType = mimeType.trim();
            }

            if (typeof isBase64 === 'boolean') {
                payload.isBase64 = isBase64;
            }

            if (typeof context !== 'undefined') {
                payload.context = context;
            }
        }

        if (!payload.content) {
            console.warn('Desktop bridge: 缺少需要保存的文件内容。');
            return false;
        }

        const posted = sendToHost(payload);
        if (!posted) {
            console.warn('Desktop bridge: 当前环境不支持宿主消息通道。');
        }
        return posted;
    },
    openExternalUrl(url) {
        if (typeof url !== 'string' || url.trim().length === 0) {
            console.warn('Desktop bridge: 缺少有效的URL。');
            return false;
        }

        const payload = {
            type: 'open-external-url',
            url: url.trim()
        };

        const posted = sendToHost(payload);
        if (!posted) {
            console.warn('Desktop bridge: 当前环境不支持宿主消息通道。');
        }
        return posted;
    },
    requestWorkspaceFolder() {
        const posted = sendToHost({ type: 'open-workspace-folder' });
        if (!posted) {
            console.warn('Desktop bridge: 当前环境不支持选择文件夹。');
        }
        return posted;
    },
    requestLoadWorkspace(rootPath) {
        if (!rootPath || typeof rootPath !== 'string') {
            console.warn('Desktop bridge: 缺少有效的工作区路径。');
            return false;
        }

        const posted = sendToHost({
            type: 'load-workspace-folder',
            rootPath
        });

        if (!posted) {
            console.warn('Desktop bridge: 当前环境不支持加载工作区。');
        }
        return posted;
    },
    saveWorkspace(rootPath, files) {
        if (!rootPath || typeof rootPath !== 'string') {
            console.warn('Desktop bridge: 缺少有效的工作区路径。');
            return false;
        }

        if (!Array.isArray(files)) {
            console.warn('Desktop bridge: 工作区文件列表无效。');
            return false;
        }

        const posted = sendToHost({
            type: 'save-workspace',
            rootPath,
            files
        });

        if (!posted) {
            console.warn('Desktop bridge: 当前环境不支持保存工作区。');
        }
        return posted;
    },
    saveWorkspaceFile(rootPath, file) {
        if (!rootPath || typeof rootPath !== 'string') {
            console.warn('Desktop bridge: 缺少有效的工作区路径。');
            return false;
        }

        if (!file || typeof file !== 'object') {
            console.warn('Desktop bridge: 缺少需要保存的文件信息。');
            return false;
        }

        const posted = sendToHost({
            type: 'save-workspace-file',
            rootPath,
            file
        });

        if (!posted) {
            console.warn('Desktop bridge: 当前环境不支持保存文件。');
        }
        return posted;
    }
};

if (!window.desktopBridge) {
    window.desktopBridge = desktopBridge;
}

if (!Array.isArray(window.__avaloniaPendingMessages)) {
    window.__avaloniaPendingMessages = [];
}

window.__dispatchMessageCallback = message => dispatchIncomingMessage(message);
flushPendingMessages();

export default desktopBridge;
