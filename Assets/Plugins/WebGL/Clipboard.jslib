mergeInto(LibraryManager.library, {
    PenguiseCopyText: function (text, callback) {
        var value = UTF8ToString(text);
        if (!window.isSecureContext || !navigator.clipboard) {
            {{{ makeDynCall('vi', 'callback') }}}(0);
            return;
        }
        try {
            navigator.clipboard.writeText(value).then(
                function () { {{{ makeDynCall('vi', 'callback') }}}(1); },
                function () { {{{ makeDynCall('vi', 'callback') }}}(0); }
            );
        } catch (error) {
            {{{ makeDynCall('vi', 'callback') }}}(0);
        }
    }
});
