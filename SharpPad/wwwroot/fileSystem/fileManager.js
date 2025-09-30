// 文件系统管理模块
import { showNotification, DEFAULT_CODE } from '../utils/common.js';

class FileManager {
    constructor() {
        // 如果已经存在实例，则返回该实例
        if (FileManager.instance) {
            return FileManager.instance;
        }

        // 如果不存在实例，则创建新实例
        FileManager.instance = this;

        this.fileListItems = document.getElementById('fileListItems');
        this.initializeEventListeners();
    }

    // 获取单例实例的静态方法
    static getInstance() {
        if (!FileManager.instance) {
            FileManager.instance = new FileManager();
        }
        return FileManager.instance;
    }

    initializeEventListeners() {
        // 文件过滤器监听
        const fileFilter = document.getElementById('fileFilter');
        if (fileFilter) {
            fileFilter.addEventListener('keyup', (e) => this.filterFiles(e.target.value));
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

        // 初始化右键菜单事件
        this.initializeContextMenus();
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

    addFolder() {
        const folderName = prompt('请输入文件夹名称：', 'New Folder');
        if (!folderName) return;

        const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
        const newFolder = {
            id: Date.now().toString(),
            name: folderName,
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

    renameFolder() {
        const menu = document.getElementById('folderContextMenu');
        const folderId = menu.getAttribute('data-folder-id');
        menu.style.display = 'none';

        if (!folderId) return;

        const filesData = localStorage.getItem('controllerFiles');
        const files = filesData ? JSON.parse(filesData) : [];

        // 递归查找文件夹
        const findAndRenameFolder = (items) => {
            for (let item of items) {
                if (item.id === folderId) {
                    const newFolderName = prompt('请输入新的文件夹名称', item.name);
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
                    if (findAndRenameFolder(item.files)) return true;
                }
            }
            return false;
        };

        findAndRenameFolder(files);
    }

    deleteFolder(folder) {
        if (confirm(`确定要删除文件夹 "${folder.name}" 及其所有内容吗？`)) {
            const files = JSON.parse(localStorage.getItem('controllerFiles') || '[]');
            this.removeFileById(files, folder.id);
            localStorage.setItem('controllerFiles', JSON.stringify(files));
            this.loadFileList();
        }
    }

    openFile(file) {
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
            window.editor.setValue(fileContent || file.content || '');
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
            nugetConfig: {
                packages: []
            }
        };
        files.push(newFile);
        localStorage.setItem('controllerFiles', JSON.stringify(files));
        this.displayFileList(files);
        this.openFile(newFile);
    }

    renameFile(file) {
        const newName = prompt('请输入新的文件名:', file.name);
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

    deleteFile(file) {
        if (confirm(`确定要删除文件 "${file.name}" 吗？`)) {
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

    renameFile() {
        const menu = document.getElementById('fileContextMenu');
        const fileId = menu.getAttribute('data-target-file-id');
        menu.style.display = 'none';

        if (!fileId) return;

        // 获取当前文件列表
        const filesData = localStorage.getItem('controllerFiles');
        const files = filesData ? JSON.parse(filesData) : [];

        // 递归查找并重命名文件
        const findAndRenameFile = (items) => {
            for (let i = 0; i < items.length; i++) {
                if (items[i].id === fileId) {
                    const newFileName = prompt('请输入新的文件名：', items[i].name);
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
                    if (findAndRenameFile(items[i].files)) return true;
                }
            }
            return false;
        };

        findAndRenameFile(files);
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

    deleteModel(modelId, showConfirm = true) {
        if (showConfirm && !confirm('确定要删除这个模型吗？')) {
            return;
        }

        const models = JSON.parse(localStorage.getItem('chatModels') || '[]');
        const filteredModels = models.filter(model => model.id !== modelId);
        localStorage.setItem('chatModels', JSON.stringify(filteredModels));

        this.updateModelList();
        this.updateModelSelect();
    }

    addFileToFolder(folderId) {
        // 关闭右键菜单
        document.getElementById('folderContextMenu').style.display = 'none';
        document.getElementById('rootContextMenu').style.display = 'none';

        const fileName = prompt('请输入文件名：', 'New File.cs');
        if (!fileName) return;

        const newFile = {
            id: this.generateUUID(),
            name: fileName,
            content: DEFAULT_CODE
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
            const newFileElement = document.querySelector(`[data-file-id="${newFile.id}"]`);
            if (newFileElement) {
                newFileElement.classList.add('selected');
                const fileContent = localStorage.getItem(`file_${newFile.id}`);
                if (fileContent && window.editor) {
                    window.editor.setValue(fileContent);
                }
            }
        }, 0);
    }

    addFolderToFolder(parentFolderId) {
        // 关闭右键菜单
        document.getElementById('folderContextMenu').style.display = 'none';
        document.getElementById('rootContextMenu').style.display = 'none';

        const folderName = prompt('请输入文件夹名称：', 'New Folder');
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
                    // 创建一个包含文件夹内容的对象
                    const folderData = {
                        name: item.name,
                        type: 'folder',
                        files: []
                    };

                    // 递归获取文件夹的所有文件内容
                    const getFilesContent = (folder, targetArray) => {
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
                                    targetArray.push({
                                        name: file.name,
                                        content: file.content,
                                        nugetConfig: file.nugetConfig
                                    });
                                }
                            });
                        }
                    };

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
        };

        findFolder(files);
    }

    importFolder() {
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

        fileInput.onchange = (e) => {
            const file = e.target.files[0];
            if (!file) {
                document.body.removeChild(fileInput);
                return;
            }

            const reader = new FileReader();
            reader.onload = (e) => {
                try {
                    const importedData = JSON.parse(e.target.result);

                    // 验证导入的数据格式
                    if (!importedData.name || !importedData.type || !Array.isArray(importedData.files)) {
                        throw new Error('无效的文件格式');
                    }

                    const filesData = localStorage.getItem('controllerFiles');
                    const files = filesData ? JSON.parse(filesData) : [];

                    // 递归生成新的 ID
                    const regenerateIds = (items) => {
                        return items.map(item => {
                            const newItem = { ...item, id: this.generateUUID() };
                            if (item.type === 'folder' && Array.isArray(item.files)) {
                                newItem.files = regenerateIds(item.files);
                            }
                            return newItem;
                        });
                    };

                    // 递归保存文件内容
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

                    // 查找目标文件夹并添加导入的内容
                    const findAndAddToFolder = (items) => {
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
                    };

                    if (findAndAddToFolder(files)) {
                        localStorage.setItem('controllerFiles', JSON.stringify(files));

                        // 保存当前展开的文件夹，并添加目标文件夹
                        const expandedFolders = this.saveExpandedFolders();
                        if (!expandedFolders.includes(targetFolderId)) {
                            expandedFolders.push(targetFolderId);
                        }

                        this.loadFileList();

                        // 恢复展开状态（包括新导入的目标文件夹）
                        this.restoreExpandedFolders(expandedFolders);

                        showNotification('导入成功', 'success');
                    }

                } catch (error) {
                    console.error('导入错误:', error);
                    showNotification('导入失败: ' + error.message, 'error');
                }
                document.body.removeChild(fileInput);
            };

            reader.onerror = () => {
                showNotification('读取文件失败', 'error');
                document.body.removeChild(fileInput);
            };

            reader.readAsText(file);
        };

        fileInput.click();
    }

    saveFileToLocalStorage(fileId, code) {
        try {
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

            showNotification('保存成功', 'success');
            return true;
        } catch (error) {
            console.error('Error saving to localStorage:', error);
            showNotification('保存失败: ' + error.message, 'error');
            return false;
        }
    }

    saveCode(code) {
        try {
            const selectedFileElement = document.querySelector('#fileListItems a.selected');
            const fileId = selectedFileElement?.getAttribute('data-file-id');

            // 如果没有选择文件，提示用户先选择或创建新文件
            if (!fileId) {
                const createNew = confirm('没有选择文件。是否要创建新文件？');
                if (!createNew) {
                    showNotification('请先选择一个文件', 'warning');
                    return;
                }

                const newFileId = this.generateUUID();
                const fileName = prompt('请输入文件名称：', 'New File.cs');
                if (!fileName) {
                    showNotification('取消创建新文件', 'info');
                    return;
                }

                const newFile = {
                    id: newFileId,
                    name: fileName,
                    content: code,
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
                    const newFileElement = document.querySelector(`[data-file-id="${newFileId}"]`);
                    if (newFileElement) {
                        newFileElement.classList.add('selected');
                    }
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

}

export { FileManager }; 
