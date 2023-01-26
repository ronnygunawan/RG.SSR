const Counter = () => {
    const [count, setCount] = React.useState(0);

    return <div>
        <h1>Counter</h1>
        <p>Button has been clicked {count} times.</p>
        <button className="btn btn-primary" onClick={() => setCount(count + 1)}>Click me</button>
    </div>;
}