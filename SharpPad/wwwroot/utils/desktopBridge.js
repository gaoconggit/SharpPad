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
