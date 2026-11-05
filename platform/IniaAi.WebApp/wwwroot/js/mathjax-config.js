// MudBlazor.Markdown draws math in the Markdown view of a block response with its bundled MathJax 3,
// which reads this object when it loads. The response is model output, so the TeX commands that make
// links or set classes and styles (\href, \class, \style, \cssId, from the "html" extension) stay off,
// and so does reading <math> elements, whose href would also become a link. MudBlazor.Markdown does not
// ship the "html" extension: without this object one such command fails to load it and stops every
// formula on the page from drawing.
window.MathJax = {
    startup: { input: ['tex'] },
    tex: {
        autoload: { html: [] },
        require: { allow: { html: false } },
    },
};
