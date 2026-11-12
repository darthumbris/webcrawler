let css = [];
const styleSheets = Array.from(document.styleSheets).filter(
	(styleSheet) => !styleSheet.href || styleSheet.href.startsWith(window.location.origin)
);
for (const sheet of styleSheets) {
	try
	{
		if (sheet instanceof CSSStyleSheet && sheet.cssRules) {
			for (const rules of Array.from(sheet.cssRules)) {
				css.push(rules.cssText);
				if (css.length >= 3000) {
					break;
				}
			}
		}
	}
	catch {}
}
return css.join('\n');