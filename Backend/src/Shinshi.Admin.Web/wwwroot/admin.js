// 운영 웹 보조 스크립트. 외부 파일로만 로드한다(CSP script-src 'self'). 서버 토큰·비밀값은 다루지 않는다
(function () {
    "use strict";
    var lastFocus = null;

    function copyFallback(text) {
        var area = document.createElement("textarea");
        area.value = text;
        area.setAttribute("readonly", "");
        area.style.position = "fixed";
        area.style.opacity = "0";
        document.body.appendChild(area);
        area.select();
        var ok = false;
        try { ok = document.execCommand("copy"); } catch (e) { ok = false; }
        document.body.removeChild(area);
        return ok;
    }

    window.shinshiAdmin = {
        // 클립보드 API는 보안 컨텍스트에서만 되므로 실패하면 선택 복사로 대체한다
        copy: function (text) {
            if (navigator.clipboard && window.isSecureContext) {
                return navigator.clipboard.writeText(text).then(function () { return true; },
                    function () { return copyFallback(text); });
            }
            return Promise.resolve(copyFallback(text));
        },
        openModal: function (id) {
            lastFocus = document.activeElement;
            var dialog = document.getElementById(id);
            if (dialog) { dialog.focus(); }
        },
        closeModal: function () {
            if (lastFocus && document.contains(lastFocus)) { lastFocus.focus(); }
            lastFocus = null;
        },
        focus: function (id) {
            var element = document.getElementById(id);
            if (element) { element.focus(); }
        }
    };

    var FOCUSABLE = "a[href], button:not([disabled]), input:not([disabled]):not([type='hidden']), select:not([disabled]), "
        + "textarea:not([disabled]), summary, [tabindex]:not([tabindex='-1'])";

    // 모달이 열려 있으면 Tab 이동을 모달 안에서 순환시킨다. summary와 details 안의 요소도 후보다
    document.addEventListener("keydown", function (event) {
        if (event.key !== "Tab") { return; }
        var dialog = document.querySelector(".modal[aria-modal='true']");
        if (!dialog) { return; }
        var items = Array.prototype.filter.call(dialog.querySelectorAll(FOCUSABLE), function (element) {
            return element.getClientRects().length > 0;
        });
        if (items.length === 0) { event.preventDefault(); dialog.focus(); return; }
        var first = items[0];
        var last = items[items.length - 1];
        var active = document.activeElement;
        var inside = dialog.contains(active) && active !== dialog;
        if (event.shiftKey && (!inside || active === first)) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && (!inside || active === last)) {
            event.preventDefault();
            first.focus();
        }
    });
})();
