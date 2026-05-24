// ES Module component - demonstrates import/export syntax with RG.SSR
// JSX source version (compiled to Greeting.js for module evaluation)
import { createElement } from 'preact';
import { formatGreeting, formatList } from './Shared/formatting.js';

export default function Greeting(props) {
    const greeting = formatGreeting(props.name);
    const features = formatList(props.features);

    return <div className="card mt-4">
        <div className="card-body">
            <h2 className="card-title">{greeting}</h2>
            <p className="card-text">
                This component uses ES module imports. Features: {features}.
            </p>
            <div className="alert alert-info mt-3">
                <strong>How it works: </strong>
                This component is written as an ES module with import/export syntax.
                The server-side renderer automatically detects the module syntax and evaluates it
                using V8's native ES module support, resolving all imports from embedded resources.
            </div>
        </div>
    </div>;
}
