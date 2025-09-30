// API 请求服务
export async function sendRequest(type, request) {
    let endPoint;
    switch (type) {
        case 'complete': endPoint = '/completion/complete'; break;
        case 'signature': endPoint = '/completion/signature'; break;
        case 'hover': endPoint = '/completion/hover'; break;
        case 'codeCheck': endPoint = '/completion/codeCheck'; break;
        case 'format': endPoint = '/completion/format'; break;
        case 'definition': endPoint = '/completion/definition'; break;
        case 'semanticTokens': endPoint = '/completion/semanticTokens'; break;
        case 'run': endPoint = '/api/coderun/run'; break;
        case 'buildExe': endPoint = '/api/coderun/buildExe'; break;
        case 'addPackages': endPoint = '/completion/addPackages'; break;
        case 'codeActions': endPoint = '/completion/codeActions'; break;
        // Multi-file endpoints
        case 'multiFileComplete': endPoint = '/completion/multiFileComplete'; break;
        case 'multiFileCodeCheck': endPoint = '/completion/multiFileCodeCheck'; break;
        case 'multiFileSignature': endPoint = '/completion/multiFileSignature'; break;
        case 'multiFileHover': endPoint = '/completion/multiFileHover'; break;
        case 'multiFileSemanticTokens': endPoint = '/completion/multiFileSemanticTokens'; break;
        default: throw new Error(`Unknown request type: ${type}`);
    }

    // 延迟超过1秒后才显示加载中样式
    const notification = document.getElementById('notification');
    const showNotificationDelay = 1000; // 1 second
    let showNotificationTimer = setTimeout(() => {
        notification.textContent = '处理中...';
        notification.style.backgroundColor = 'rgba(33, 150, 243, 0.9)';
        notification.style.display = 'block';
    }, showNotificationDelay);

    try {
        if (type === 'run' || type === 'buildExe') {
            // 使用 POST 请求发送代码和包信息，支持流式响应
            const response = await fetch(endPoint, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(request)
            });

            clearTimeout(showNotificationTimer);
            notification.style.display = 'none';
            return {
                reader: response.body.getReader(),
                showNotificationTimer
            };
        } else {
            const response = await fetch(endPoint, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(request)
            });

            clearTimeout(showNotificationTimer);
            notification.style.display = 'none';

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            // Handle 204 No Content responses
            if (response.status === 204) {
                // Return an object with data as null to maintain consistent return structure
                return { data: null };
            }

            // For responses with content, parse the JSON
            const data = await response.json();
            return { data };
        }
    } catch (error) {
        clearTimeout(showNotificationTimer);
        notification.textContent = '请求失败: ' + error.message;
        notification.style.backgroundColor = 'rgba(244, 67, 54, 0.9)';
        notification.style.display = 'block';
        throw error;
    }
} 
