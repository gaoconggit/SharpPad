// 公共工具函数
export function getCurrentFile() {
    const files = JSON.parse(localStorage.getItem('files') || '[]');
    const currentFileId = localStorage.getItem('currentFileId');
    return files.find(file => file.id === currentFileId);
}

export function layoutEditor() {
    if (window.editor) {
        setTimeout(() => {
            window.editor.layout();
        }, 200);
    }
} 