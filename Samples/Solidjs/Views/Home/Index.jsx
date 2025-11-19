const Index = () => {
    return {
        tag: "div",
        props: { className: "text-center" },
        children: [
            { tag: "h1", props: { className: "display-4" }, children: ["Welcome"] },
            { 
                tag: "p", 
                props: {}, 
                children: [
                    "Learn about ",
                    { 
                        tag: "a", 
                        props: { href: "https://docs.microsoft.com/aspnet/core" }, 
                        children: ["building Web apps with ASP.NET Core"] 
                    },
                    "."
                ] 
            }
        ]
    };
}
