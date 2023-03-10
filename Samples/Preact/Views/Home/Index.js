"use strict";

var Index = function Index() {
    return React.createElement(
        "div",
        { className: "text-center" },
        React.createElement(
            "h1",
            { className: "display-4" },
            "Welcome"
        ),
        React.createElement(
            "p",
            null,
            "Learn about ",
            React.createElement(
                "a",
                { href: "https://docs.microsoft.com/aspnet/core" },
                "building Web apps with ASP.NET Core"
            ),
            "."
        )
    );
};

