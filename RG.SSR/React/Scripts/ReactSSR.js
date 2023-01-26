const createElement = (tag, props, ...children) => ({
    tag,
    props,
    children
});

const useState = (initialState) => [
    initialState,
    () => { }
];

const useEffect = () => { };

const useContext = () => { };

const useReducer = (reducer, initialState) => [
    initialState,
    () => { }
];

const useCallback = (callback) => callback;

const useMemo = (callback) => callback();

const useRef = (initialValue) => ({ current: initialValue });

const React = {
    createElement,
    useState,
    useEffect,
    useContext,
    useReducer,
    useCallback,
    useMemo,
    useRef
};

const render = (vdom) => {
    if (vdom == null) {
        return "";
    }
    if (typeof vdom === "string") {
        return vdom;
    }
    if (typeof vdom === "number") {
        return vdom.toString();
    }
    if (typeof vdom === "boolean") {
        return vdom.toString();
    }

    const { tag, props, children } = vdom;

    if (tag == null || tag === "") {
        if (children == null) {
            return "";
        }
        return children.map(child => render(child)).join("");
    }

    let propList = "";
    for (const key in props) {
        if (key.startsWith("on")) continue;
        if (key === "className") {
            propList += ` class="${props[key]}"`;
        }
        propList += ` ${key}="${props[key]}"`;
    }

    let result = `<${tag}${propList}>`;
    if (children != null) {
        result += children.map(child => render(child)).join("");
    }
    result += `</${tag}>`;

    return result;
}