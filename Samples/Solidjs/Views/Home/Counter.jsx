const Counter = () => {
    const [count, setCount] = solidjs.createSignal(0);

    return {
        tag: "div",
        props: {},
        children: [
            { tag: "h1", props: {}, children: ["Counter"] },
            { 
                tag: "p", 
                props: {}, 
                children: [`Button has been clicked ${count()} times.`] 
            },
            {
                tag: "button",
                props: { className: "btn btn-primary", onClick: () => setCount(count() + 1) },
                children: ["Click me"]
            }
        ]
    };
};
