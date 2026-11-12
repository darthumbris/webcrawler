let failSafe = 0, depth = 5, arrayDepth = 10;

var getKeys = (obj, base, parents) => {
	parents = parents || [];

	// Failsafe, prevent endless looping
	if (++failSafe > 50000) {
		console.error('stopped loop on', base, obj, parents);
		throw 'stopped loop';
	}

	// Prevent alias loops
	if (!obj) return;
	if (!obj.__uid__ && !isSimpleType(obj)) Object.defineProperty(obj, '__uid__', {
		value: newId(),
		writable: false,
		enumerable: false
	});

	// Loop trough object values
	return Object.keys(obj).map(key => {
		let baseKey = !base ? key : [base, key].join('.')

		try {
			let value = isSimpleType(obj[key]) ? obj[key] : null;
			let baseParents = parents ? parents.concat([obj]) : [];
			let children = (obj[key]
				&& !isSimpleType(obj[key])
				&& (isNaN(key) || parseInt(key) < arrayDepth)
				&& (obj.__uid__ && !parents.some(x => x.__uid__ == obj.__uid__))
				&& baseKey.split('.').length < depth
				&& (obj[key].constructor && obj[key].constructor.name != 'Window')
				&& !isElement(obj[key]))
				? getKeys(obj[key], baseKey, baseParents) : [];

			return {
				key: baseKey,
				value,
				type: typeof obj[key],
				constructor: obj[key] && obj[key].constructor && obj[key].constructor.name,
				children
			};
		}
		catch (e) { return { key: baseKey, children: [] } }
	});
};
var toArray = (obj) => {
	var result = {};
	obj.forEach(itm => {
		//result.push([itm.key, itm.value]);
		result[itm.key] = itm.value;

		var children = toArray(itm.children);
		result = Object.assign(result, children);
		//    result = result.concat(children);
	});
	return result;
}
var newId = () => {
	return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
		var r = Math.random() * 16 | 0, v = c == 'x' ? r : (r & 0x3 | 0x8);
		return v.toString(16);
	});
};
var isSimpleType = (obj) => ['function', 'object'].indexOf(typeof obj) == -1;
var isElement = (obj) => {
	try {
		//Using W3 DOM2 (works for FF, Opera and Chrome)
		return obj instanceof HTMLElement || obj.constructor.name == 'HTMLIFrameElement';
	}
	catch (e) {
		//Browsers not supporting W3 DOM2 don't have HTMLElement and
		//an exception is thrown and we end up here. Testing some
		//properties that all elements have (works on IE7)
		return (typeof obj === "object") &&
			(obj.nodeType === 1) && (typeof obj.style === "object") &&
			(typeof obj.ownerDocument === "object");
	}
}
		
var windowData = "[]";
try { windowData = JSON.stringify(toArray(getKeys(window))); } catch (e) { }

return windowData;