function trackHistory() {
    window.changeUrl = function(url) {
        history.pushState(null, "", url);
    }
}

export { trackHistory };