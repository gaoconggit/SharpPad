// 文件系统管理模块
import { showNotification, DEFAULT_CODE, PROJECT_TYPE_CHANGE_EVENT } from '../utils/common.js';
import { customPrompt, customConfirm } from '../utils/customPrompt.js';
import desktopBridge from '../utils/desktopBridge.js';
import { fileListResizer } from './fileListResizer.js';
import { strToU8, zipSync } from '../libs/fflate.min.js';

class FileManager {
    constructor() {
        // 如果已经存在实例，则返回该实例
        if (FileManager.instance) {
            return FileManager.instance;
        }

        // 如果不存在实例，则创建新实例
        FileManager.instance = this;

        this.fileListItems = document.getElementById('fileListItems');
        this.workspaceRootPath = localStorage.getItem('workspaceRootPath') || null;
        this.persistTimer = null;
        this.storageHookInstalled = false;
        this.installStorageHooks();
        this.initializeEventListeners();
    }

    // 获取单例实例的静态方法
    static getInstance() {
        if (!FileManager.instance) {
            FileManager.instance = new FileManager();
        }
        return FileManager.instance;
    }

    installStorageHooks() {
        if (this.storageHookInstalled || typeof localStorage?.setItem !== 'function') {
            return;
        }

        const originalSetItem = localStorage.setItem.bind(localStorage);
        localStorage.setItem = (key, value) => {
            const result = originalSetItem(key, value);
            const instance = FileManager.instance;
            if (instance && (key === 'controllerFiles' || (typeof key === 'string' && key.startsWith('file_')))) {
                instance.schedulePersistToDisk(key);
            }
            return result;
        };

        this.storageHookInstalled = true;
    }

    initializeEventListeners() {
        // 文件过滤器监听
        const fileFilter = document.getElementById('fileFilter');
        if (fileFilter) {
            fileFilter.addEventListener('keyup', (e) => this.filterFiles(e.target.value));
        }

        // 打开文件夹监听
        const openFolderBtn = document.getElementById('openFolderBtn');
        if (openFolderBtn) {
            openFolderBtn.addEventListener('click', () => this.selectAndOpenFolder());
        }

        // 添加文件按钮监听
        const addFileBtn = document.getElementById('addFileBtn');
        if (addFileBtn) {
            addFileBtn.addEventListener('click', () => this.addFile());
        }

        // 添加文件夹按钮监听
        const addFolderBtn = document.getElementById('addFolderBtn');
        if (addFolderBtn) {
            addFolderBtn.addEventListener('click', () => this.addFolder());
        }

        // 文件列表折叠/展开按钮监听
        const toggleFileListBtn = document.getElementById('toggleFileList');
        if (toggleFileListBtn) {
            toggleFileListBtn.addEventListener('click', () => this.toggleFileList());
        }

        // 恢复文件列表按钮监听
        const restoreFileListBtn = document.querySelector('.restore-filelist');
        if (restoreFileListBtn) {
            restoreFileListBtn.addEventListener('click', () => this.toggleFileList());
        }

        // 初始化文件列表状态
        this.initializeFileListState();

        // 初始化右键菜单事件
        this.initializeContextMenus();
    }

    initializeFileListState() {
        const fileList = document.getElementById('fileList');
        const minimizedButton = document.querySelector('.minimized-filelist-button');
        const isCollapsed = localStorage.getItem('fileListCollapsed') === 'true';

        if (isCollapsed && fileList && minimizedButton) {
            fileList.classList.add('collapsed');
            minimizedButton.style.display = 'block';
        } else if (minimizedButton) {
            minimizedButton.style.display = 'none';
        }

        if (fileListResizer && typeof fileListResizer.updateContainerWidth === 'function') {
            fileListResizer.updateContainerWidth();
        }
    }

    toggleFileList() {
        const fileList = document.getElementById('fileList');
        const minimizedButton = document.querySelector('.minimized-filelist-button');

        if (!fileList || !minimizedButton) return;

        const isCollapsed = fileList.classList.toggle('collapsed');

        if (isCollapsed) {
            minimizedButton.style.display = 'block';
            localStorage.setItem('fileListCollapsed', 'true');
        } else {
            minimizedButton.style.display = 'none';
            localStorage.setItem('fileListCollapsed', 'false');
        }

        if (fileListResizer && typeof fileListResizer.updateContainerWidth === 'function') {
            fileListResizer.updateContainerWidth();
        }
    }

    schedulePersistToDisk(reason) {
        if (!this.workspaceRootPath) {
            return;
        }

        clearTimeout(this.persistTimer);
        this.persistTimer = setTimeout(() => {
            this.persistWorkspaceToDisk(reason);
        }, 600);
    }

    buildWorkspaceSnapshot() {
        const filesData = localStorage.getItem('controllerFiles');
        const files = filesData ? JSON.parse(filesData) : [];

        const buildNodes = (items, parentPath = '') => {
            if (!Array.isArray(items)) {
                return [];
            }

            return items.map(item => {
                const relativePath = parentPath ? `${parentPath}/${item.name}` : item.name;

                if (item.type === 'folder') {
                    return {
                        id: item.id,
                        name: item.name,
                        type: 'folder',
                        path: relativePath,
                        files: buildNodes(item.files || [], relativePath),
                        projectType: item.projectType,
                        nugetConfig: item.nugetConfig
                    };
                }

                const content = localStorage.getItem(`file_${item.id}`) ?? item.content ?? '';
                return {
                    id: item.id,
                    name: item.name,
                    type: 'file',
                    path: relativePath,
                    content,
                    projectType: item.projectType,
                    nugetConfig: item.nugetConfig
                };
            });
        };

        return buildNodes(files);
    }

    async persistWorkspaceToDisk(reason) {
        if (!this.workspaceRootPath) {
            return;
        }

        const payload = {
            rootPath: this.workspaceRootPath,
            files: this.buildWorkspaceSnapshot(),
            pruneExtra: true
        };

        try {
            const response = await fetch('/api/filesystem/sync', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(payload)
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            const data = await response.json().catch(() => ({}));
            if (data && data.success !== false) {
                if (reason === 'manual') {
                    showNotification('已同步到磁盘', 'success');
                }
                return;
            }

            throw new Error(data?.message || '同步失败');
        } catch (error) {
            console.error('同步到磁盘失败:', error);
            if (reason === 'manual') {
                showNotification(`同步失败: ${error.message}`, 'error');
            }
        }
    }

    setWorkspaceRoot(rootPath) {
        if (!rootPath) {
            return;
        }
        this.workspaceRootPath = rootPath;
        localStorage.setItem('workspaceRootPath', rootPath);
    }

    clearFileCache() {
        const keys = [];
        for (let i = 0; i < localStorage.length; i++) {
            const key = localStorage.key(i);
            if (key && key.startsWith('file_')) {
                keys.push(key);
            }
        }

        keys.forEach(key => localStorage.removeItem(key));
    }

    normalizeWorkspaceItems(items, parentPath = '') {
        if (!Array.isArray(items)) {
            return [];
        }

        return items
            .filter(item => item && item.name)
            .map(item => {
                const relativePath = parentPath ? `${parentPath}/${item.name}` : item.name;
                const id = item.id || this.generateUUID();

                if (item.type === 'folder') {
                    return {
                        ...item,
                        id,
                        type: 'folder',
                        path: relativePath,
                        files: this.normalizeWorkspaceItems(item.files || [], relativePath)
                    };
                }

                const content = typeof item.content === 'string' ? item.content : '';
                localStorage.setItem(`file_${id}`, content);

                return {
                    ...item,
                    id,
                    type: 'file',
                    path: relativePath,
                    content
                };
            });
    }

    findFirstFile(items) {
        for (const item of items) {
            if (item.type === 'folder' && Array.isArray(item.files)) {
                const child = this.findFirstFile(item.files);
                if (child) {
                    return child;
                }
            } else if (item.type !== 'folder') {
                return item;
            }
        }
        return null;
    }

    async loadWorkspaceFromDisk(folderPath) {
        if (!folderPath) {
            showNotification('请选择有效的文件夹路径', 'warning');
            return;
        }

        try {
            const response = await fetch('/api/filesystem/open-folder', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ path: folderPath })
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            const payload = await response.json();
            const files = Array.isArray(payload?.files) ? payload.files : [];

            this.clearFileCache();
            const normalized = this.normalizeWorkspaceItems(files);

            localStorage.setItem('controllerFiles', JSON.stringify(normalized));
            this.setWorkspaceRoot(payload?.rootPath || folderPath);

            const expandedFolders = this.saveExpandedFolders();
            this.loadFileList();
            this.restoreExpandedFolders(expandedFolders);

            const firstFile = this.findFirstFile(normalized);
            if (firstFile) {
                setTimeout(() => this.openFile(firstFile), 0);
            }

            showNotification('已从磁盘加载项目', 'success');
        } catch (error) {
            console.error('加载文件夹失败:', error);
            showNotification(`打开文件夹失败: ${error.message}`, 'error');
        }
    }

    handleFolderPicked(message) {
        if (!message || message.type !== 'pick-folder-completed') {
            return false;
        }

        if (message.cancelled) {
            showNotification('已取消选择文件夹', 'info');
            return true;
        }

        if (!message.success || !message.path) {
            showNotification(message.message || '打开文件夹失败', 'error');
            return true;
        }

        this.loadWorkspaceFromDisk(message.path);
        return true;
    }

    async requestFolderPath() {
        if (desktopBridge?.isAvailable) {
            const posted = desktopBridge.requestFolderPick({ source: 'file-manager-open' });
            if (!posted) {
                showNotification('桌面环境不可用，无法选择文件夹', 'error');
            }
            return null;
        }

        const manualPath = await customPrompt('请输入要打开的文件夹路径：');
        return manualPath ? manualPath.trim() : null;
    }

    async selectAndOpenFolder() {
        const path = await this.requestFolderPath();
        if (!path) {
            return;
        }
        await this.loadWorkspaceFromDisk(path);
    }

    normalizeProjectType(projectType) {
        const fallback = 'console';
        const candidate = typeof projectType === 'string'
            ? projectType.trim().toLowerCase()
            : '';

        const select = document.getElementById('projectTypeSelect');
        if (select) {
            const allowed = Array.from(select.options).map(opt => opt.value);
            if (candidate && allowed.includes(candidate)) {
                return candidate;
            }

            if (allowed.includes(fallback)) {
                return fallback;
            }

            return allowed[0] || fallback;
        }

        const allowedFallback = ['console', 'winforms', 'webapi', 'avalonia'];
        if (candidate && allowedFallback.includes(candidate)) {
            return candidate;
        }

        return allowedFallback[0];
    }

    getActiveProjectType() {
        try {
            const select = document.getElementById('projectTypeSelect');
            if (select && select.value) {
                return this.normalizeProjectType(select.value);
            }

            if (typeof window !== 'undefined' && window.localStorage) {
                const stored = window.localStorage.getItem('sharpPad.projectType');
                if (stored) {
                    return this.normalizeProjectType(stored);
                }
            }
        } catch (error) {
            console.warn('无法获取当前项目类型偏好:', error);
        }

        return this.normalizeProjectType();
    }

    updateFileProjectType(fileId, projectType) {
        if (!fileId) {
            return false;
        }

        const normalized = this.normalizeProjectType(projectType);
        let updated = false;

        try {
            const filesData = window.localStorage.getItem('controllerFiles');
            if (filesData) {
                const files = JSON.parse(filesData);
                const target = this.findFileById(files, fileId);
                if (target && target.projectType !== normalized) {
                    target.projectType = normalized;
                    window.localStorage.setItem('controllerFiles', JSON.stringify(files));
                    updated = true;
                }
            }
        } catch (error) {
            console.error('更新项目类型失败:', error);
        }

        try {
            window.localStorage.setItem('sharpPad.projectType', normalized);
        } catch (storageError) {
            console.warn('无法保存项目类型偏好:', storageError);
        }

        return updated;
    }

    initializeContextMenus() {
        // 文件右键菜单
        const fileContextMenu = document.getElementById('fileContextMenu');
        if (fileContextMenu) {
            // 重命名文件
            fileContextMenu.querySelector('.rename')?.addEventListener('click', () => {
                const fileId = fileContextMenu.getAttribute('data-target-file-id');
                const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
                const file = this.findFileById(files, fileId);
                if (file) {
                    this.renameFile(file);
                }
                fileContextMenu.style.display = 'none';
            });

            // 删除文件
            fileContextMenu.querySelector('.delete')?.addEventListener('click', () => {
                const fileId = fileContextMenu.getAttribute('data-target-file-id');
                const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
                const file = this.findFileById(files, fileId);
                if (file) {
                    this.deleteFile(file);
                }
                fileContextMenu.style.display = 'none';
            });
        }

        // 文件夹右键菜单
        const folderContextMenu = document.getElementById('folderContextMenu');
        if (folderContextMenu) {
            // 重命名文件夹
            folderContextMenu.querySelector('.rename')?.addEventListener('click', () => {
                const folderId = folderContextMenu.getAttribute('data-folder-id');
                const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
                const folder = this.findFileById(files, folderId);
                if (folder) {
                    this.renameFolder(folder);
                }
                folderContextMenu.style.display = 'none';
            });

            // 删除文件夹
            folderContextMenu.querySelector('.delete')?.addEventListener('click', () => {
                const folderId = folderContextMenu.getAttribute('data-folder-id');
                const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
                const folder = this.findFileById(files, folderId);
                if (folder) {
                    this.deleteFolder(folder);
                }
                folderContextMenu.style.display = 'none';
            });
        }

        // 点击其他地方关闭菜单
        document.addEventListener('click', (e) => {
            if (!e.target.closest('.context-menu')) {
                const menus = document.querySelectorAll('.context-menu');
                menus.forEach(menu => menu.style.display = 'none');
            }
        });
    }

    // 保存展开的文件夹状态
    saveExpandedFolders() {
        const expandedFolders = [];
        document.querySelectorAll('.folder-content.open').forEach(content => {
            const folderId = content.getAttribute('data-folder-content');
            if (folderId) {
                expandedFolders.push(folderId);
            }
        });
        return expandedFolders;
    }

    // 恢复文件夹展开状态
    restoreExpandedFolders(expandedFolders) {
        expandedFolders.forEach(folderId => {
            const content = document.querySelector(`[data-folder-content="${folderId}"]`);
            const header = document.querySelector(`[data-folder-header="${folderId}"]`);
            if (content && header) {
                content.classList.add('open');
                header.classList.add('open');
            }
        });
    }

    loadFileList() {
        const expandedFolders = this.saveExpandedFolders(); // 保存当前展开状态
        const filesData = localStorage.getItem('controllerFiles');
        if (filesData) {
            try {
                const files = JSON.parse(filesData);
                this.displayFileList(files);
                this.restoreExpandedFolders(expandedFolders); // 恢复展开状态
            } catch (error) {
                console.error('Error parsing files from localStorage:', error);
            }
        } else {
            this.displayFileList([]);
        }
    }

    displayFileList(files) {
        if (!this.fileListItems) return;

        this.fileListItems.innerHTML = '';
        files.forEach(file => {
            const element = this.createFileElement(file);
            this.fileListItems.appendChild(element);
        });
    }

    createFileElement(file) {
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
                this.moveFileToFolder(draggedId, file.id);
            });

            // 添加点击展开/折叠功能
            folderHeader.addEventListener('click', (e) => {
                if (e.target === folderHeader) {
                    this.toggleFolder(file.id);
                }
            });

            // 添加右键菜单
            folderHeader.addEventListener('contextmenu', (e) => {
                e.preventDefault();
                this.showFolderContextMenu(e, file);
            });

            const folderContent = document.createElement('div');
            folderContent.className = 'folder-content';
            folderContent.setAttribute('data-folder-content', file.id);

            // 递归创建子文件和文件夹
            if (file.files) {
                file.files.forEach(childFile => {
                    const childElement = this.createFileElement(childFile);
                    folderContent.appendChild(childElement);
                });
            }

            folderDiv.appendChild(folderHeader);
            folderDiv.appendChild(folderContent);
            li.appendChild(folderDiv);
        } else {
            // 创建文件链接
            const fileContainer = document.createElement('div');
            fileContainer.className = 'file-container';
            fileContainer.style.display = 'flex';
            fileContainer.style.alignItems = 'center';
            fileContainer.style.gap = '5px';

            const a = document.createElement('a');
            a.href = '#';
            a.className = 'file-name';
            a.textContent = file.name;
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
                this.openFile(file);
            });

            a.addEventListener('contextmenu', (e) => {
                e.preventDefault();
                this.showContextMenu(e, file);
            });

            fileContainer.appendChild(a);
            li.appendChild(fileContainer);
        }

        return li;
    }

    toggleFolder(folderId) {
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

    moveFileToFolder(fileId, folderId) {
        const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
        const file = this.findFileById(files, fileId);
        const folder = this.findFileById(files, folderId);

        if (file && folder && folder.type === 'folder') {
            // 从原位置移除文件
            this.removeFileById(files, fileId);
            // 添加到新文件夹
            folder.files = folder.files || [];
            folder.files.push(file);
            localStorage.setItem('controllerFiles', JSON.stringify(files));
            this.loadFileList();
        }
    }

    findFileById(files, id) {
        for (const file of files) {
            if (file.id === id) return file;
            if (file.type === 'folder' && file.files) {
                const found = this.findFileById(file.files, id);
                if (found) return found;
            }
        }
        return null;
    }

    removeFileById(files, id) {
        for (let i = 0; i < files.length; i++) {
            if (files[i].id === id) {
                files.splice(i, 1);
                return true;
            }
            if (files[i].type === 'folder' && files[i].files) {
                if (this.removeFileById(files[i].files, id)) return true;
            }
        }
        return false;
    }

    async addFolder() {
        try {
            const folderName = await customPrompt('请输入文件夹名称：', 'New Folder');
            if (!folderName || folderName.trim() === '') {
                console.log('用户取消或输入为空');
                return;
            }

            const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
            const newFolder = {
                id: Date.now().toString(),
                name: folderName.trim(),
                type: 'folder',
                files: []
            };

            files.push(newFolder);
            localStorage.setItem('controllerFiles', JSON.stringify(files));

            // 保存当前展开的文件夹，并添加新文件夹
            const expandedFolders = this.saveExpandedFolders();
            expandedFolders.push(newFolder.id);

            // 刷新文件列表并恢复展开状态
            this.loadFileList();
            this.restoreExpandedFolders(expandedFolders);

            showNotification('文件夹创建成功', 'success');
            console.log('文件夹创建成功:', newFolder.name);
        } catch (error) {
            console.error('创建文件夹失败:', error);
            showNotification('创建文件夹失败: ' + error.message, 'error');
        }
    }

    showFolderContextMenu(e, folder) {
        e.preventDefault();
        const menu = document.getElementById('folderContextMenu');
        menu.style.display = 'block';
        
        // 先设置初始位置以获取菜单尺寸
        menu.style.left = e.pageX + 'px';
        menu.style.top = e.pageY + 'px';
        
        // 获取菜单尺寸和视窗尺寸
        const menuRect = menu.getBoundingClientRect();
        const viewportWidth = window.innerWidth;
        const viewportHeight = window.innerHeight;
        
        // 计算调整后的位置
        let left = e.pageX;
        let top = e.pageY;
        
        // 如果菜单右边界超出视窗，则向左调整
        if (left + menuRect.width > viewportWidth) {
            left = viewportWidth - menuRect.width - 5; // 5px 的边距
        }
        
        // 如果菜单下边界超出视窗，则向上调整
        if (top + menuRect.height > viewportHeight) {
            top = viewportHeight - menuRect.height - 5; // 5px 的边距
        }
        
        // 确保菜单不会超出左边界和上边界
        if (left < 5) left = 5;
        if (top < 5) top = 5;
        
        // 应用调整后的位置
        menu.style.left = left + 'px';
        menu.style.top = top + 'px';
        menu.setAttribute('data-folder-id', folder.id);
    }

    async renameFolder() {
        const menu = document.getElementById('folderContextMenu');
        const folderId = menu.getAttribute('data-folder-id');
        menu.style.display = 'none';

        if (!folderId) return;

        const filesData = localStorage.getItem('controllerFiles');
        const files = filesData ? JSON.parse(filesData) : [];

        // 递归查找文件夹
        const findAndRenameFolder = async (items) => {
            for (let item of items) {
                if (item.id === folderId) {
                    const newFolderName = await customPrompt('请输入新的文件夹名称', item.name);
                    if (!newFolderName || newFolderName === item.name) return;

                    item.name = newFolderName;
                    localStorage.setItem('controllerFiles', JSON.stringify(files));

                    // 保存当前展开的文件夹状态
                    const expandedFolders = this.saveExpandedFolders();

                    // 刷新文件列表并恢复展开状态
                    this.loadFileList();
                    this.restoreExpandedFolders(expandedFolders);

                    showNotification('重命名成功', 'success');
                    return true;
                }
                if (item.type === 'folder' && item.files) {
                    if (await findAndRenameFolder(item.files)) return true;
                }
            }
            return false;
        };

        await findAndRenameFolder(files);
    }

    async deleteFolder(folder) {
        if (await customConfirm(`确定要删除文件夹 "${folder.name}" 及其所有内容吗？`)) {
            const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
            this.removeFileById(files, folder.id);
            localStorage.setItem('controllerFiles', JSON.stringify(files));
            this.loadFileList();
        }
    }

    async openFile(file) {
        const currentSelected = this.fileListItems.querySelector('a.selected');
        const currentFileId = currentSelected?.getAttribute('data-file-id');
        if (currentFileId && currentFileId !== file.id && window.editor) {
            this.saveFileToLocalStorage(currentFileId, window.editor.getValue(), { silent: true });
        }

        // 移除之前的选中状态
        const selectedFile = this.fileListItems.querySelector('.selected');
        if (selectedFile) {
            selectedFile.classList.remove('selected');
        }

        // 设置新的选中状态
        const fileLink = this.fileListItems.querySelector(`a[data-file-id="${file.id}"]`);
        if (fileLink) {
            fileLink.classList.add('selected');
        }

        // 从localStorage获取最新的文件内容并更新编辑器
        if (window.editor) {
            const fileContent = localStorage.getItem(`file_${file.id}`);
            window.__suppressAutoSave = true;
            window.editor.setValue(fileContent || file.content || '');
            window.__suppressAutoSave = false;
        }

        if (window.editorInstance?.loadBreakpointsForFile) {
            window.editorInstance.loadBreakpointsForFile(file.id);
        }

        const normalizedType = this.normalizeProjectType(file?.projectType);
        file.projectType = normalizedType;
        this.updateFileProjectType(file.id, normalizedType);

        const projectTypeSelect = document.getElementById('projectTypeSelect');
        if (projectTypeSelect) {
            const currentValue = (projectTypeSelect.value || '').toLowerCase();
            if (currentValue !== normalizedType) {
                projectTypeSelect.value = normalizedType;
                window.dispatchEvent(new CustomEvent(PROJECT_TYPE_CHANGE_EVENT, {
                    detail: { projectType: normalizedType }
                }));
            }
        }

        // 检查文件是否需要恢复 NuGet 包
        if (file.nugetConfig && Array.isArray(file.nugetConfig.packages) && file.nugetConfig.packages.length > 0) {
            await this.checkAndRestoreFilePackages(file);
        }
    }

    async checkAndRestoreFilePackages(file) {
        try {
            // 导入 API 服务
            const { sendRequest } = await import('../utils/apiService.js');

            const packages = file.nugetConfig.packages;
            if (!packages || packages.length === 0) {
                return;
            }

            // 静默检查包是否存在，只在需要时才恢复
            const packagesToRestore = [];

            for (const pkg of packages) {
                // 这里可以添加检查包是否已下载的逻辑
                // 目前简化处理：尝试添加包，后端会处理已存在的情况
                packagesToRestore.push(pkg);
            }

            if (packagesToRestore.length === 0) {
                return;
            }

            // 批量恢复包（静默模式）
            const request = {
                Packages: packagesToRestore.map(pkg => ({
                    Id: pkg.id,
                    Version: pkg.version
                })),
                SourceKey: 'nuget' // 使用默认源
            };

            const result = await sendRequest('addPackages', request);

            if (result?.data && result.data.code === 0) {
                // 静默成功，不显示通知
                console.log(`文件 ${file.name} 的 NuGet 包已检查并恢复`);
            }
        } catch (error) {
            // 静默失败，只记录日志
            console.warn(`检查文件 ${file.name} 的 NuGet 包时出错:`, error);
        }
    }

    showContextMenu(e, file) {
        e.preventDefault();
        const menu = document.getElementById('fileContextMenu');
        menu.style.display = 'block';
        
        // 先设置初始位置以获取菜单尺寸
        menu.style.left = e.pageX + 'px';
        menu.style.top = e.pageY + 'px';
        
        // 获取菜单尺寸和视窗尺寸
        const menuRect = menu.getBoundingClientRect();
        const viewportWidth = window.innerWidth;
        const viewportHeight = window.innerHeight;
        
        // 计算调整后的位置
        let left = e.pageX;
        let top = e.pageY;
        
        // 如果菜单右边界超出视窗，则向左调整
        if (left + menuRect.width > viewportWidth) {
            left = viewportWidth - menuRect.width - 5; // 5px 的边距
        }
        
        // 如果菜单下边界超出视窗，则向上调整
        if (top + menuRect.height > viewportHeight) {
            top = viewportHeight - menuRect.height - 5; // 5px 的边距
        }
        
        // 确保菜单不会超出左边界和上边界
        if (left < 5) left = 5;
        if (top < 5) top = 5;
        
        // 应用调整后的位置
        menu.style.left = left + 'px';
        menu.style.top = top + 'px';
        menu.setAttribute('data-target-file-id', file.id);
    }

    addFile() {
        const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
        const newFile = {
            id: Date.now().toString(),
            name: 'New File.cs',
            content: DEFAULT_CODE,
            projectType: this.getActiveProjectType(),
            nugetConfig: {
                packages: []
            }
        };
        files.push(newFile);
        localStorage.setItem('controllerFiles', JSON.stringify(files));
        this.displayFileList(files);
        this.openFile(newFile);
    }

    async renameFile(file) {
        const newName = await customPrompt('请输入新的文件名:', file.name);
        if (newName && newName !== file.name) {
            const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
            const targetFile = this.findFileById(files, file.id);
            if (targetFile) {
                targetFile.name = newName;
                localStorage.setItem('controllerFiles', JSON.stringify(files));
                this.loadFileList();
            }
        }
    }

    async deleteFile(file) {
        if (await customConfirm(`确定要删除文件 "${file.name}" 吗？`)) {
            const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
            this.removeFileById(files, file.id);
            localStorage.setItem('controllerFiles', JSON.stringify(files));
            this.loadFileList();

            // 如果删除的是当前选中的文件，清空编辑器
            const selectedFileElement = document.querySelector('#fileListItems a.selected');
            if (selectedFileElement?.getAttribute('data-file-id') === file.id) {
                if (window.editor) {
                    window.editor.setValue('');
                }
            }
        }
    }

    filterFiles(filter) {
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

    async renameFile() {
        const menu = document.getElementById('fileContextMenu');
        const fileId = menu.getAttribute('data-target-file-id');
        menu.style.display = 'none';

        if (!fileId) return;

        // 获取当前文件列表
        const filesData = localStorage.getItem('controllerFiles');
        const files = filesData ? JSON.parse(filesData) : [];

        // 递归查找并重命名文件
        const findAndRenameFile = async (items) => {
            for (let i = 0; i < items.length; i++) {
                if (items[i].id === fileId) {
                    const newFileName = await customPrompt('请输入新的文件名：', items[i].name);
                    if (!newFileName || newFileName === items[i].name) return;

                    // 更新文件名
                    items[i].name = newFileName;

                    // 保存更新后的文件列表
                    localStorage.setItem('controllerFiles', JSON.stringify(files));

                    // 刷新文件列表
                    this.loadFileList();

                    // 选中重命名的文件
                    setTimeout(() => {
                        document.querySelector(`[data-file-id="${fileId}"]`)?.classList.add('selected');
                    }, 0);

                    showNotification('重命名成功', 'success');
                    return true;
                }
                if (items[i].type === 'folder' && items[i].files) {
                    if (await findAndRenameFile(items[i].files)) return true;
                }
            }
            return false;
        };

        await findAndRenameFile(files);
    }

    initializeContextMenu() {
        const menu = document.getElementById('fileContextMenu');

        // 绑定删除事件
        menu.querySelector('.delete').addEventListener('click', () => this.deleteFile());

        // 绑定重命名事件
        menu.querySelector('.rename').addEventListener('click', () => this.renameFile());

        // 点击其他地方关闭菜单
        document.addEventListener('click', (e) => {
            if (!menu.contains(e.target)) {
                menu.style.display = 'none';
            }
        });
    }

    moveItem(itemId, direction) {
        const filesData = localStorage.getItem('controllerFiles');
        if (!filesData) return;

        const files = JSON.parse(filesData);

        // 递归查找并移动项目
        const findAndMoveItem = (items) => {
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
        };

        if (findAndMoveItem(files)) {
            localStorage.setItem('controllerFiles', JSON.stringify(files));
            const expandedFolders = this.saveExpandedFolders();
            this.loadFileList();
            this.restoreExpandedFolders(expandedFolders);
        }
    }

    generateUUID() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            const r = Math.random() * 16 | 0;
            const v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }

    findParentFolder(files, fileId) {
        for (const file of files) {
            if (file.type === 'folder') {
                if (file.files.some(f => f.id === fileId)) {
                    return file;
                }
                const found = this.findParentFolder(file.files, fileId);
                if (found) return found;
            }
        }
        return null;
    }

    duplicateFile() {
        const menu = document.getElementById('fileContextMenu');
        const fileId = menu.getAttribute('data-target-file-id');
        const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');

        const originalFile = this.findFileById(files, fileId);
        if (!originalFile) return;

        // 创建新文件对象
        const newFile = {
            id: this.generateUUID(),
            name: `${originalFile.name} (副本)`,
            content: originalFile.content,
            projectType: this.normalizeProjectType(originalFile.projectType),
            nugetConfig: originalFile.nugetConfig || { packages: [] }
        };

        const parentFolder = this.findParentFolder(files, originalFile.id);
        if (parentFolder) {
            parentFolder.files.push(newFile);
        } else {
            files.push(newFile);
        }

        // 保存文件列表
        localStorage.setItem('controllerFiles', JSON.stringify(files));

        // 刷新文件列表显示
        this.loadFileList();

        // 关闭上下文菜单
        const contextMenus = document.querySelectorAll('.context-menu');
        contextMenus.forEach(menu => {
            menu.style.display = 'none';
        });
    }

    duplicateFolder() {
        const menu = document.getElementById('folderContextMenu');
        const folderId = menu.getAttribute('data-folder-id');
        menu.style.display = 'none';

        if (!folderId) return;

        const filesData = localStorage.getItem('controllerFiles');
        const files = filesData ? JSON.parse(filesData) : [];

        // 递归查找并复制文件夹
        const findAndDuplicateFolder = (items) => {
            for (let item of items) {
                if (item.id === folderId) {
                    // 深度复制文件夹及其内容
                    const duplicatedFolder = JSON.parse(JSON.stringify(item));

                    // 为复制的文件夹及其所有子文件生成新的ID
                    const generateNewIds = (folder) => {
                        folder.id = this.generateUUID();
                        if (folder.files) {
                            folder.files.forEach(file => {
                                if (file.type === 'folder') {
                                    generateNewIds(file);
                                } else {
                                    const oldId = file.id;
                                    file.id = this.generateUUID();
                                    // 复制文件内容
                                    const fileContent = localStorage.getItem(`file_${oldId}`);
                                    if (fileContent) {
                                        localStorage.setItem(`file_${file.id}`, fileContent);
                                    }
                                }
                            });
                        }
                    };

                    generateNewIds(duplicatedFolder);
                    duplicatedFolder.name = `${item.name} (副本)`;

                    // 将复制的文件夹添加到同级目录
                    const parentArray = items;
                    const index = parentArray.findIndex(i => i.id === folderId);
                    parentArray.splice(index + 1, 0, duplicatedFolder);

                    // 保存更新后的文件列表
                    localStorage.setItem('controllerFiles', JSON.stringify(files));

                    // 刷新文件列表并展开复制的文件夹
                    const expandedFolders = this.saveExpandedFolders();
                    expandedFolders.push(duplicatedFolder.id);
                    this.loadFileList();
                    this.restoreExpandedFolders(expandedFolders);

                    return true;
                }
                if (item.type === 'folder' && item.files) {
                    if (findAndDuplicateFolder(item.files)) return true;
                }
            }
            return false;
        };

        findAndDuplicateFolder(files);
    }

    moveOutOfFolder() {
        // 获取要移动的文件ID
        const menu = document.getElementById('fileContextMenu');
        const fileId = menu.getAttribute('data-target-file-id');
        menu.style.display = 'none';

        // 获取存储的文件列表
        let files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');

        // 递归查找文件和其文件夹
        const findFileAndParentFolder = (items, parentFolder = null) => {
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
        };

        // 查找文件和文件夹
        const result = findFileAndParentFolder(files);
        if (result) {
            // 从原文件夹中移除
            result.parentArray.splice(result.index, 1);
            // 添加到根目录
            files.push(result.file);
            // 保存更新后的文件列表
            localStorage.setItem('controllerFiles', JSON.stringify(files));
            // 重新加载文件列表
            this.loadFileList();
        }
    }

    configureNuGet() {
        const menu = document.getElementById('fileContextMenu');
        const fileId = menu.getAttribute('data-target-file-id');
        window.currentFileId = fileId; // 设置当前文件ID为全局变量

        // 从 localStorage 获取文件列表
        const filesData = localStorage.getItem('controllerFiles');
        const files = filesData ? JSON.parse(filesData) : [];

        // 递归查找目标文件
        const findFile = (items) => {
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
        };

        const file = findFile(files);
        if (!file) return;

        if (window.nugetManager) {
            window.nugetManager.open(file);
            return;
        }

        const dialog = document.getElementById('nugetConfigDialog');
        dialog.style.display = 'block';
    }

    async deleteModel(modelId, showConfirm = true) {
        if (showConfirm && !(await customConfirm('确定要删除这个模型吗？'))) {
            return;
        }

        const models = JSON.parse(localStorage.getItem('chatModels') || '[]');
        const filteredModels = models.filter(model => model.id !== modelId);
        localStorage.setItem('chatModels', JSON.stringify(filteredModels));

        this.updateModelList();
        this.updateModelSelect();
    }

    async addFileToFolder(folderId) {
        // 关闭右键菜单
        document.getElementById('folderContextMenu').style.display = 'none';
        document.getElementById('rootContextMenu').style.display = 'none';

        const fileName = await customPrompt('请输入文件名：', 'New File.cs');
        if (!fileName) return;

        const newFile = {
            id: this.generateUUID(),
            name: fileName,
            content: DEFAULT_CODE,
            projectType: this.getActiveProjectType(),
            nugetConfig: {
                packages: []
            }
        };

        const filesData = localStorage.getItem('controllerFiles');
        const files = filesData ? JSON.parse(filesData) : [];

        // 递归查找目标文件夹
        const findFolder = (items) => {
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
        };

        findFolder(files);

        // 保存文件列表和文件内容
        localStorage.setItem('controllerFiles', JSON.stringify(files));
        localStorage.setItem(`file_${newFile.id}`, newFile.content);

        // 保存当前展开的文件夹，并添加目标文件夹
        const expandedFolders = this.saveExpandedFolders();
        if (!expandedFolders.includes(folderId)) {
            expandedFolders.push(folderId);
        }

        // 刷新文件列表并恢复展开状态
        this.loadFileList();
        this.restoreExpandedFolders(expandedFolders);

        // 选中新建的文件
        setTimeout(() => {
            this.openFile(newFile);
        }, 0);
    }

    async addFolderToFolder(parentFolderId) {
        // 关闭右键菜单
        document.getElementById('folderContextMenu').style.display = 'none';
        document.getElementById('rootContextMenu').style.display = 'none';

        const folderName = await customPrompt('请输入文件夹名称：', 'New Folder');
        if (!folderName) return;

        const newFolder = {
            id: this.generateUUID(),
            name: folderName,
            type: 'folder',
            files: []
        };

        const filesData = localStorage.getItem('controllerFiles');
        const files = filesData ? JSON.parse(filesData) : [];

        // 递归查找目标文件夹
        const findFolder = (items) => {
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
        };

        findFolder(files);

        // 保存文件列表
        localStorage.setItem('controllerFiles', JSON.stringify(files));

        // 保存当前展开的文件夹，并添加父文件夹和新文件夹
        const expandedFolders = this.saveExpandedFolders();
        if (!expandedFolders.includes(parentFolderId)) {
            expandedFolders.push(parentFolderId);
        }
        expandedFolders.push(newFolder.id);

        // 刷新文件列表并恢复展开状态
        this.loadFileList();
        this.restoreExpandedFolders(expandedFolders);
    }

    exportFolder() {
        const menu = document.getElementById('folderContextMenu');
        const folderId = menu.getAttribute('data-folder-id');
        menu.style.display = 'none';

        if (!folderId) return;

        const filesData = localStorage.getItem('controllerFiles');
        const files = filesData ? JSON.parse(filesData) : [];

        // 递归查找文件夹
        const findFolder = (items) => {
            for (let item of items) {
                if (item.id === folderId) {
                    // 创建包含文件夹内容的对象（使用共享方法获取最新内容）
                    const folderData = this.createFolderStructureData(item);
                    const jsonContent = JSON.stringify(folderData, null, 2);

                    if (this.shouldUseDesktopExport()) {
                        this.exportFolderViaDesktopBridge(item.name, jsonContent);
                        return true;
                    }

                    // Web 模式：使用共享下载方法
                    this.downloadBlob(`${item.name}.json`, jsonContent, 'application/json', '文件夹已导出');
                    return true;
                }
                if (item.type === 'folder' && item.files) {
                    if (findFolder(item.files)) return true;
                }
            }
            return false;
        };

        findFolder(files);
    }

    async importFolder() {
        const menu = document.getElementById('folderContextMenu');
        const targetFolderId = menu?.getAttribute('data-folder-id');
        if (menu) {
            menu.style.display = 'none';
        }

        if (!targetFolderId) {
            return;
        }

        const preferDesktopImport = this.shouldUseDesktopImport();

        // 询问用户选择导入方式（即便在桌面环境也允许 URL 导入）
        const importMethod = await this.selectImportMethod(preferDesktopImport);
        if (!importMethod) {
            return; // 用户取消
        }

        if (importMethod === 'url') {
            await this.importFromUrl(targetFolderId);
            return;
        }

        if (preferDesktopImport) {
            this.importFolderViaDesktopBridge(targetFolderId);
        } else {
            await this.importFromFile(targetFolderId);
        }
    }

    async selectImportMethod(useDesktopImport = false) {
        return new Promise((resolve) => {
            const dialog = document.createElement('div');
            dialog.className = 'modal';
            dialog.style.display = 'block';
            dialog.innerHTML = `
                <div class="modal-content" style="max-width: 400px;">
                    <div class="modal-header">
                        <h2>选择导入方式</h2>
                    </div>
                    <div class="modal-body">
                        <div style="display: flex; flex-direction: column; gap: 10px;">
                            <button id="importFromFileBtn" class="save-button" style="width: 100%; padding: 12px;">
                                📁 从本地文件导入${useDesktopImport ? '（桌面）' : ''}
                            </button>
                            <button id="importFromUrlBtn" class="save-button" style="width: 100%; padding: 12px;">
                                🌐 从 URL 导入
                            </button>
                            <button id="importCancelBtn" class="ai-edit-cancel" style="width: 100%; padding: 12px;">
                                取消
                            </button>
                        </div>
                    </div>
                </div>
            `;

            document.body.appendChild(dialog);

            const cleanup = () => {
                document.body.removeChild(dialog);
            };

            dialog.querySelector('#importFromFileBtn').addEventListener('click', () => {
                cleanup();
                resolve('file');
            });

            dialog.querySelector('#importFromUrlBtn').addEventListener('click', () => {
                cleanup();
                resolve('url');
            });

            dialog.querySelector('#importCancelBtn').addEventListener('click', () => {
                cleanup();
                resolve(null);
            });
        });
    }

    async importFromUrl(targetFolderId) {
        try {
            const url = await customPrompt('请输入 JSON 文件的 URL:', 'https://');
            if (!url || !url.trim()) {
                return;
            }

            const trimmedUrl = url.trim();

            // 简单的 URL 验证
            if (!trimmedUrl.startsWith('http://') && !trimmedUrl.startsWith('https://')) {
                showNotification('请输入有效的 URL (必须以 http:// 或 https:// 开头)', 'error');
                return;
            }

            showNotification('正在从 URL 获取数据...', 'info');

            // 使用 fetch 获取 URL 内容
            const response = await fetch(trimmedUrl);

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const contentType = response.headers.get('content-type');
            if (contentType && !contentType.includes('application/json') && !contentType.includes('text/')) {
                showNotification('警告: 该 URL 可能不是 JSON 文件', 'warning');
            }

            const jsonContent = await response.text();

            if (!jsonContent || jsonContent.trim().length === 0) {
                throw new Error('URL 返回的内容为空');
            }

            await this.applyImportedFolderData(targetFolderId, jsonContent);
        } catch (error) {
            console.error('从 URL 导入失败:', error);

            let errorMessage = '从 URL 导入失败: ';
            if (error.message.includes('Failed to fetch') || error.message.includes('NetworkError')) {
                errorMessage += '网络请求失败，请检查 URL 是否正确或存在跨域限制';
            } else {
                errorMessage += error.message;
            }

            showNotification(errorMessage, 'error');
        }
    }

    async importFromFile(targetFolderId) {
        const fileInput = document.createElement('input');
        fileInput.type = 'file';
        fileInput.accept = '.json';

        fileInput.style.position = 'fixed';
        fileInput.style.top = '-100px';
        fileInput.style.left = '-100px';
        fileInput.style.width = '1px';
        fileInput.style.height = '1px';
        fileInput.style.opacity = '0';
        fileInput.style.pointerEvents = 'none';

        document.body.appendChild(fileInput);

        fileInput.onchange = (e) => {
            const file = e.target.files[0];
            if (!file) {
                document.body.removeChild(fileInput);
                return;
            }

            const reader = new FileReader();
            reader.onload = async (evt) => {
                try {
                    await this.applyImportedFolderData(targetFolderId, evt.target.result);
                } catch (error) {
                    console.error('导入错误:', error);
                    showNotification('导入失败: ' + error.message, 'error');
                } finally {
                    document.body.removeChild(fileInput);
                }
            };

            reader.onerror = () => {
                showNotification('读取文件失败', 'error');
                document.body.removeChild(fileInput);
            };

            reader.readAsText(file);
        };

        setTimeout(() => {
            try {
                fileInput.click();
            } catch (e) {
                const clickEvent = new MouseEvent('click', {
                    view: window,
                    bubbles: true,
                    cancelable: true
                });
                fileInput.dispatchEvent(clickEvent);
            }
        }, 0);
    }

    shouldUseDesktopImport() {
        if (!desktopBridge?.isAvailable) {
            return false;
        }

        try {
            const navPlatform = (navigator?.platform || '').toLowerCase();
            const navAgent = (navigator?.userAgent || '').toLowerCase();
            if (navPlatform.includes('mac') || navAgent.includes('macintosh')) {
                return true;
            }

            const hostPlatform = desktopBridge.platform?.();
            if (typeof hostPlatform === 'string' && hostPlatform.toLowerCase().includes('mac')) {
                return true;
            }
        } catch (error) {
            console.warn('检测桌面导入环境失败:', error);
        }

        return false;
    }

    shouldUseDesktopExport() {
        return this.shouldUseDesktopImport();
    }

    exportFolderViaDesktopBridge(folderName, jsonContent) {
        const sanitizedName = this.sanitizeFileName(folderName, 'export');
        const fileName = `${sanitizedName}.json`;
        
        this.requestDesktopSave(fileName, jsonContent, 'application/json', 'export-folder', {
            folderName: sanitizedName
        });
    }

    importFolderViaDesktopBridge(targetFolderId) {
        if (!desktopBridge?.isAvailable) {
            showNotification('当前环境不支持桌面导入。', 'warning');
            return;
        }

        try {
            desktopBridge.requestPickAndUpload(undefined, {
                action: 'import-folder',
                folderId: targetFolderId
            });
            showNotification('请选择要导入的 JSON 文件', 'info');
        } catch (error) {
            console.error('桌面导入请求失败:', error);
            showNotification('无法发起导入请求: ' + error.message, 'error');
        }
    }

    handleDesktopUpload(message) {
        if (!message || message.type !== 'pick-and-upload-completed') {
            return false;
        }

        let context = message.context;
        if (typeof context === 'string') {
            try {
                context = JSON.parse(context);
            } catch {
                context = { raw: context };
            }
        }

        if (!context || typeof context !== 'object') {
            context = {};
        }

        const action = context?.action;
        if (action !== 'import-folder') {
            return false;
        }

        const folderId = context?.folderId;
        if (!folderId) {
            showNotification('导入失败: 缺少目标文件夹信息。', 'error');
            return true;
        }

        if (message.cancelled) {
            showNotification('已取消导入', 'info');
            return true;
        }

        if (!message.success) {
            showNotification(message.message || '导入失败', 'error');
            return true;
        }

        let rawContent = null;
        const payload = message.payload;
        if (typeof payload === 'string') {
            rawContent = payload;
        } else if (payload && typeof payload === 'object') {
            if (typeof payload.content === 'string') {
                rawContent = payload.content;
            } else {
                try {
                    rawContent = JSON.stringify(payload);
                } catch {
                    rawContent = null;
                }
            }
        }

        if (!rawContent) {
            showNotification('导入失败: 未收到文件内容。', 'error');
            return true;
        }

        this.applyImportedFolderData(folderId, rawContent).catch(error => {
            console.error('桌面导入失败:', error);
            showNotification('导入失败: ' + error.message, 'error');
        });

        return true;
    }

    handleDesktopDownload(message) {
        if (!message || message.type !== 'download-file-completed') {
            return false;
        }

        let context = message.context;
        if (typeof context === 'string') {
            try {
                context = JSON.parse(context);
            } catch {
                context = { raw: context };
            }
        }

        if (!context || typeof context !== 'object') {
            context = {};
        }

        const action = context?.action;
        if (!action) {
            return false;
        }

        if (message.cancelled) {
            let cancelMessage = '已取消';
            if (action === 'build-download') {
                cancelMessage = '已取消导出发布包';
            } else if (action === 'save-file-as' || action === 'save-folder-as') {
                cancelMessage = '已取消保存';
            } else {
                cancelMessage = '已取消导出';
            }
            showNotification(cancelMessage, 'info');
            return true;
        }

        if (!message.success) {
            let errorMessage = message.message || '操作失败';
            if (action === 'build-download') {
                errorMessage = message.message || '导出发布包失败';
            } else if (action === 'save-file-as' || action === 'save-folder-as') {
                errorMessage = message.message || '保存失败';
            } else {
                errorMessage = message.message || '导出失败';
            }
            showNotification(errorMessage, 'error');
            return true;
        }

        const savedPath = typeof message.savedPath === 'string' && message.savedPath.trim().length > 0
            ? message.savedPath
            : null;

        if (action === 'build-download') {
            if (savedPath) {
                showNotification(`发布包已保存到: ${savedPath}`, 'success');
            } else {
                showNotification('发布包已导出', 'success');
            }
            return true;
        }

        if (action === 'export-folder') {
            if (savedPath) {
                showNotification(`已保存到: ${savedPath}`, 'success');
            } else {
                showNotification('文件夹已导出', 'success');
            }
            return true;
        }

        if (action === 'save-file-as') {
            if (savedPath) {
                showNotification(`文件已保存到: ${savedPath}`, 'success');
            } else {
                showNotification('文件已保存', 'success');
            }
            return true;
        }

        if (action === 'save-folder-as') {
            if (savedPath) {
                showNotification(`文件夹已保存到: ${savedPath}`, 'success');
            } else {
                showNotification('文件夹已保存', 'success');
            }
            return true;
        }

        return false;
    }

    async applyImportedFolderData(targetFolderId, jsonContent) {
        if (!jsonContent) {
            throw new Error('导入数据为空');
        }

        const importedData = JSON.parse(jsonContent);

        if (!importedData.name || !importedData.type || !Array.isArray(importedData.files)) {
            throw new Error('无效的文件格式');
        }

        const filesData = localStorage.getItem('controllerFiles');
        const files = filesData ? JSON.parse(filesData) : [];

        const regenerateIds = (items) => {
            return items.map(item => {
                const newItem = { ...item, id: this.generateUUID() };
                if (item.type === 'folder' && Array.isArray(item.files)) {
                    newItem.files = regenerateIds(item.files);
                }
                return newItem;
            });
        };

        const saveFileContents = (items) => {
            items.forEach(item => {
                if (item.type !== 'folder' && item.content) {
                    localStorage.setItem(`file_${item.id}`, item.content);
                }
                if (item.type === 'folder' && Array.isArray(item.files)) {
                    saveFileContents(item.files);
                }
            });
        };

        const findAndAddToFolder = (items) => {
            for (const item of items) {
                if (item.id === targetFolderId) {
                    if (!item.files) {
                        item.files = [];
                    }
                    const importedFiles = regenerateIds(importedData.files);
                    saveFileContents(importedFiles);
                    item.files.push(...importedFiles);
                    return true;
                }
                if (item.type === 'folder' && item.files) {
                    if (findAndAddToFolder(item.files)) {
                        return true;
                    }
                }
            }
            return false;
        };

        if (!findAndAddToFolder(files)) {
            throw new Error('未找到目标文件夹。');
        }

        localStorage.setItem('controllerFiles', JSON.stringify(files));

        const expandedFolders = this.saveExpandedFolders();
        if (!expandedFolders.includes(targetFolderId)) {
            expandedFolders.push(targetFolderId);
        }

        this.loadFileList();
        this.restoreExpandedFolders(expandedFolders);

        showNotification('导入成功', 'success');
        await this.restoreNuGetPackages(importedData.files);

        return true;
    }

    async restoreNuGetPackages(items) {
        // 收集所有需要恢复的包
        const packagesToRestore = new Map();

        const collectPackages = (files) => {
            files.forEach(item => {
                if (item.type === 'folder' && Array.isArray(item.files)) {
                    collectPackages(item.files);
                } else if (item.nugetConfig && Array.isArray(item.nugetConfig.packages)) {
                    item.nugetConfig.packages.forEach(pkg => {
                        const key = `${pkg.id}@${pkg.version}`.toLowerCase();
                        if (!packagesToRestore.has(key)) {
                            packagesToRestore.set(key, pkg);
                        }
                    });
                }
            });
        };

        collectPackages(items);

        if (packagesToRestore.size === 0) {
            return;
        }

        // 显示恢复进度通知
        const packages = Array.from(packagesToRestore.values());
        const packageCount = packages.length;

        // 创建友好的包列表预览（最多显示3个）
        const packagePreview = packages.slice(0, 3).map(pkg => pkg.id).join(', ');
        const moreText = packageCount > 3 ? ` 等 ${packageCount} 个` : '';

        showNotification(`正在恢复 NuGet 包: ${packagePreview}${moreText}...`, 'info');

        try {
            // 导入 API 服务
            const { sendRequest } = await import('../utils/apiService.js');

            // 批量恢复包
            const request = {
                Packages: packages.map(pkg => ({
                    Id: pkg.id,
                    Version: pkg.version
                })),
                SourceKey: 'nuget' // 使用默认源
            };

            const result = await sendRequest('addPackages', request);

            if (result?.data && result.data.code === 0) {
                showNotification(`✓ 成功恢复 ${packageCount} 个 NuGet 包`, 'success');
            } else {
                throw new Error(result?.data?.message || '恢复失败');
            }
        } catch (error) {
            console.error('恢复 NuGet 包失败:', error);
            showNotification(`✗ NuGet 包恢复失败: ${error.message}`, 'error');
        }
    }

    saveFileToLocalStorage(fileId, code, options = {}) {
        try {
            const { silent = false } = options;
            // 保存到 localStorage
            localStorage.setItem(`file_${fileId}`, code);

            // 保存到文件列表
            const filesData = localStorage.getItem('controllerFiles');
            const files = filesData ? JSON.parse(filesData) : [];

            const updateFileContent = (files, targetId, newCode) => {
                for (const file of files) {
                    if (file.id === targetId) {
                        file.content = newCode;
                        return;
                    }
                    if (file.type === 'folder' && file.files) {
                        updateFileContent(file.files, targetId, newCode);
                    }
                }
            };

            updateFileContent(files, fileId, code);
            localStorage.setItem('controllerFiles', JSON.stringify(files));     

            if (!silent) {
                showNotification('保存成功', 'success');
            }
            return true;
        } catch (error) {
            console.error('Error saving to localStorage:', error);
            showNotification('保存失败: ' + error.message, 'error');
            return false;
        }
    }

    async saveCode(code) {
        try {
            const selectedFileElement = document.querySelector('#fileListItems a.selected');
            const fileId = selectedFileElement?.getAttribute('data-file-id');

            // 如果没有选择文件，提示用户先选择或创建新文件
            if (!fileId) {
                const createNew = await customConfirm('没有选择文件。是否要创建新文件？');
                if (!createNew) {
                    showNotification('请先选择一个文件', 'warning');
                    return;
                }

                const newFileId = this.generateUUID();
                const fileName = await customPrompt('请输入文件名称：', 'New File.cs');
                if (!fileName) {
                    showNotification('取消创建新文件', 'info');
                    return;
                }

                const newFile = {
                    id: newFileId,
                    name: fileName,
                    content: code,
                    projectType: this.getActiveProjectType(),
                    nugetConfig: {
                        packages: []
                    }
                };

                const filesData = localStorage.getItem('controllerFiles');
                const files = filesData ? JSON.parse(filesData) : [];
                files.push(newFile);
                localStorage.setItem('controllerFiles', JSON.stringify(files));
                localStorage.setItem(`file_${newFileId}`, code);

                this.loadFileList();

                // 选中新创建的文件
                setTimeout(() => {
                    this.openFile(newFile);
                }, 0);

                showNotification('新文件创建成功', 'success');
                return;
            }

            // 使用新的公共方法保存文件
            this.saveFileToLocalStorage(fileId, code);
        } catch (error) {
            console.error('Error saving to localStorage:', error);
            showNotification('保存失败: ' + error.message, 'error');
        }
    }





    showOnlyCurrentFolder(folderId) {
        // 获取 ul 元素
        var ulElement = document.getElementById('fileListItems');

        // 获取所有 ul 下的直接子 li 元素
        var liElements = ulElement.children;

        // 需要检查的 data-folder-header 值
        var targetFolderHeaderId = folderId;

        // 遍历 li 元素
        for (var i = 0; i < liElements.length; i++) {
            var li = liElements[i];
            var folderHeader = li.querySelector('.folder-header');

            // 检查当前 li 是否符合条件
            if (folderHeader && folderHeader.getAttribute('data-folder-header') === targetFolderHeaderId) {
                // 符合条件的 li 显示
                li.style.display = '';
            } else {
                // 不符合条件的 li 隐藏
                li.style.display = 'none';
            }
        }

        const menu = document.getElementById('folderContextMenu');
        menu.style.display = 'none';

        // 展开当前文件夹
        const content = document.querySelector(`[data-folder-content="${folderId}"]`);
        const header = document.querySelector(`[data-folder-header="${folderId}"]`);
        if (content && header) {
            content.classList.add('open');
            header.classList.add('open');

            // 展开所有子文件夹
            // const subFolders = content.querySelectorAll('.folder-content, .folder-header');
            // subFolders.forEach(element => {
            //     element.classList.add('open');
            // });
        }
    }

    // 另存为单个文件到磁盘
    saveFileAs() {
        const menu = document.getElementById('fileContextMenu');
        const fileId = menu.getAttribute('data-target-file-id');
        menu.style.display = 'none';

        if (!fileId) return;

        const filesData = localStorage.getItem('controllerFiles');
        const files = filesData ? JSON.parse(filesData) : [];

        // 递归查找文件
        const file = this.findFileById(files, fileId);
        if (!file) {
            showNotification('未找到文件', 'error');
            return;
        }

        // 获取文件内容
        const fileContent = localStorage.getItem(`file_${file.id}`) || file.content || '';
        
        // 使用桌面保存对话框或Web下载
        if (this.shouldUseDesktopExport()) {
            this.saveFileViaDesktopBridge(file.name, fileContent);
        } else {
            this.saveFileViaBlob(file.name, fileContent);
        }
    }

    // 清理文件名（移除非法字符，防止目录遍历）
    sanitizeFileName(fileName, defaultName = 'file') {
        if (!fileName || typeof fileName !== 'string') {
            return defaultName;
        }
        
        // 移除前后空格
        let sanitized = fileName.trim();
        
        // 移除路径分隔符和其他危险字符
        // 移除: / \ : * ? " < > | 以及控制字符
        sanitized = sanitized.replace(/[\/\\:*?"<>|\x00-\x1f\x7f]/g, '');
        
        // 移除前后的点（防止隐藏文件）
        sanitized = sanitized.replace(/^\.+|\.+$/g, '');
        
        // 如果清理后为空，使用默认名称
        if (sanitized.length === 0) {
            return defaultName;
        }
        
        // 限制长度（避免文件系统限制问题）
        if (sanitized.length > 200) {
            sanitized = sanitized.substring(0, 200);
        }
        
        return sanitized;
    }

    // 通用的桌面保存方法
    requestDesktopSave(fileName, content, mimeType, action, context = {}, isBase64 = false) {
        if (!desktopBridge?.isAvailable) {
            showNotification('当前环境不支持桌面保存。', 'warning');
            return false;
        }

        try {
            const posted = desktopBridge.requestFileDownload({
                fileName: fileName,
                content: content,
                mimeType: mimeType,
                isBase64: isBase64,
                context: { action: action, ...context }
            });

            if (!posted) {
                showNotification('无法唤起桌面保存对话框。', 'error');
            }
            return posted;
        } catch (error) {
            console.error('桌面保存请求失败:', error);
            showNotification('无法发起保存请求: ' + error.message, 'error');
            return false;
        }
    }

    // 通用的 Blob 下载方法
    downloadBlob(fileName, content, mimeType, successMessage) {
        try {
            const blob = new Blob([content], { type: mimeType });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = fileName;

            // 兼容 macOS WebView: 先添加到 DOM，设置样式，然后触发点击
            a.style.display = 'none';
            document.body.appendChild(a);

            // 使用 setTimeout 确保元素完全附加到 DOM 后再触发点击
            setTimeout(() => {
                try {
                    a.click();
                } catch (e) {
                    // 如果 click() 失败，尝试通过事件触发
                    const clickEvent = new MouseEvent('click', {
                        view: window,
                        bubbles: true,
                        cancelable: true
                    });
                    a.dispatchEvent(clickEvent);
                }

                // 延迟清理以确保下载开始
                setTimeout(() => {
                    document.body.removeChild(a);
                    URL.revokeObjectURL(url);
                }, 100);
            }, 0);

            showNotification(successMessage || '文件已保存', 'success');
        } catch (error) {
            console.error('保存失败:', error);
            showNotification('保存失败: ' + error.message, 'error');
        }
    }

    downloadBinary(fileName, bytes, mimeType, successMessage) {
        try {
            const blob = new Blob([bytes], { type: mimeType });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = fileName;
            a.style.display = 'none';
            document.body.appendChild(a);

            setTimeout(() => {
                try {
                    a.click();
                } catch (e) {
                    const clickEvent = new MouseEvent('click', {
                        view: window,
                        bubbles: true,
                        cancelable: true
                    });
                    a.dispatchEvent(clickEvent);
                }

                setTimeout(() => {
                    document.body.removeChild(a);
                    URL.revokeObjectURL(url);
                }, 100);
            }, 0);

            showNotification(successMessage || '文件已保存', 'success');
        } catch (error) {
            console.error('保存失败:', error);
            showNotification('保存失败: ' + error.message, 'error');
        }
    }

    saveFileViaDesktopBridge(fileName, content) {
        this.requestDesktopSave(fileName, content, 'text/plain', 'save-file-as');
    }

    saveFileViaBlob(fileName, content) {
        this.downloadBlob(fileName, content, 'text/plain;charset=utf-8', '文件已保存');
    }

    // 另存为文件夹到磁盘（包含完整目录结构）
    saveFolderAs() {
        const menu = document.getElementById('folderContextMenu');
        const folderId = menu.getAttribute('data-folder-id');
        menu.style.display = 'none';

        if (!folderId) return;

        const filesData = localStorage.getItem('controllerFiles');
        const files = filesData ? JSON.parse(filesData) : [];

        // 递归查找文件夹
        const folder = this.findFileById(files, folderId);
        if (!folder || folder.type !== 'folder') {
            showNotification('未找到文件夹', 'error');
            return;
        }

        // 创建包含完整目录结构的数据
        const folderData = this.createFolderStructureData(folder);
        const zipPackage = this.buildZipFromFolder(folderData);
        if (!zipPackage) {
            showNotification('打包文件夹时出现问题', 'error');
            return;
        }

        // 使用桌面保存对话框或Web下载
        if (this.shouldUseDesktopExport()) {
            this.saveFolderViaDesktopBridge(zipPackage);
        } else {
            this.saveFolderViaBlob(zipPackage);
        }
    }

    // 创建文件夹结构数据（包含最新内容和完整配置）
    // 注意：此方法获取 localStorage 中的最新内容，而不是缓存的 file.content
    // 这确保导出/保存的始终是最新保存的内容
    createFolderStructureData(folder) {
        const structure = {
            name: folder.name,
            type: 'folder',
            files: []
        };

        const processFiles = (sourceFiles, targetArray) => {
            if (!sourceFiles || !Array.isArray(sourceFiles)) return;

            sourceFiles.forEach(file => {
                if (file.type === 'folder') {
                    const subFolder = {
                        name: file.name,
                        type: 'folder',
                        files: []
                    };
                    targetArray.push(subFolder);
                    processFiles(file.files, subFolder.files);
                } else {
                    // 获取最新的文件内容（从 localStorage）
                    const content = localStorage.getItem(`file_${file.id}`) || file.content || '';
                    
                    // 包含 projectType 和 nugetConfig 以保持完整的项目配置
                    // 这些字段在导入时会被使用，确保项目设置被正确恢复
                    targetArray.push({
                        name: file.name,
                        content: content,
                        projectType: file.projectType,
                        nugetConfig: file.nugetConfig
                    });
                }
            });
        };

        processFiles(folder.files, structure.files);
        return structure;
    }

    buildZipFromFolder(folderData) {
        if (!folderData || folderData.type !== 'folder') {
            return null;
        }

        const sanitizedRoot = this.sanitizeFileName(folderData.name, 'folder');
        const zipEntries = {};

        const appendFiles = (node, currentPath) => {
            if (!node || !Array.isArray(node.files)) return;

            node.files.forEach(item => {
                if (item.type === 'folder') {
                    const folderName = this.sanitizeFileName(item.name, 'folder');
                    appendFiles(item, `${currentPath}/${folderName}`);
                } else {
                    const fileName = this.sanitizeFileName(item.name, 'file');
                    const content = typeof item.content === 'string' ? item.content : '';
                    const fullPath = `${currentPath}/${fileName}`;
                    zipEntries[fullPath] = strToU8(content);
                }
            });
        };

        appendFiles(folderData, sanitizedRoot);

        if (Object.keys(zipEntries).length === 0) {
            zipEntries[`${sanitizedRoot}/.keep`] = new Uint8Array();
        }

        const zipBytes = zipSync(zipEntries, { level: 9 });
        return {
            zipBytes,
            zipName: `${sanitizedRoot}.zip`,
            folderName: sanitizedRoot
        };
    }

    arrayBufferToBase64(buffer) {
        if (!buffer) return '';
        const bytes = buffer instanceof Uint8Array ? buffer : new Uint8Array(buffer);
        const chunkSize = 0x8000;
        let binary = '';

        for (let i = 0; i < bytes.length; i += chunkSize) {
            const chunk = bytes.subarray(i, i + chunkSize);
            binary += String.fromCharCode.apply(null, chunk);
        }

        return btoa(binary);
    }

    saveFolderViaDesktopBridge(zipPackage) {
        const base64Content = this.arrayBufferToBase64(zipPackage.zipBytes);

        const posted = this.requestDesktopSave(
            zipPackage.zipName,
            base64Content,
            'application/zip',
            'save-folder-as',
            {
                folderName: zipPackage.folderName,
                format: 'zip'
            },
            true
        );
        if (posted) {
            showNotification('文件夹已打包为 ZIP，等待保存', 'info');
        }
    }

    saveFolderViaBlob(zipPackage) {
        this.downloadBinary(zipPackage.zipName, zipPackage.zipBytes, 'application/zip', '文件夹已保存为 ZIP');
    }

}

export { FileManager }; 
