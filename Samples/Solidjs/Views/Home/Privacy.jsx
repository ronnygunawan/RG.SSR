const Privacy = ({ title }) => {
    return {
        tag: "div",
        props: {},
        children: [
            { tag: "h1", props: {}, children: [title] },
            { tag: "p", props: {}, children: ["Use this page to detail your site's privacy policy."] }
        ]
    };
};
