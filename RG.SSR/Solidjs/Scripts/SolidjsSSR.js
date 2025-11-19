const createSignal = (initialValue) => [
    () => initialValue,
    () => { }
];

const createEffect = () => { };

const createMemo = (callback) => callback;

const createResource = (fetcher) => [
    () => ({ loading: false, error: undefined }),
    { mutate: () => { }, refetch: () => { } }
];

const onMount = () => { };

const onCleanup = () => { };

const createContext = () => { };

const useContext = () => { };

const solidjs = {
    createSignal,
    createEffect,
    createMemo,
    createResource,
    onMount,
    onCleanup,
    createContext,
    useContext
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
    if (typeof vdom === "function") {
        return render(vdom());
    }

    const { tag, props, children } = vdom;

    if (tag == null || tag === "") {
        if (children == null) {
            return "";
        }
        if (Array.isArray(children)) {
            return children.map(child => render(child)).join("");
        }
        return render(children);
    }

    let propList = "";
    for (const key in props) {
        if (key.startsWith("on")) continue;
        if (key === "className") {
            propList += ` class="${props[key]}"`;
        } else {
            propList += ` ${key}="${props[key]}"`;
        }
    }

    let result = `<${tag}${propList}>`;
    if (children != null) {
        if (Array.isArray(children)) {
            result += children.map(child => render(child)).join("");
        } else {
            result += render(children);
        }
    }
    result += `</${tag}>`;

    return result;
}
