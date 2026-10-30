// 少量原生交互：确认框、下载、滚动定位
window.micaConfirm = function (message) {
    return window.confirm(message);
};

window.micaDownload = function (url) {
    const link = document.createElement('a');
    link.href = url;
    link.download = '';
    document.body.appendChild(link);
    link.click();
    link.remove();
};

window.micaScrollTableTop = function () {
    const wrap = document.querySelector('.table-wrap');
    if (wrap) wrap.scrollTop = 0;
};
