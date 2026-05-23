// ES Module component - demonstrates import/export syntax with RG.SSR
import { createElement } from 'react';
import { formatGreeting, formatList } from './formatting.js';

export default function Greeting(props) {
    const greeting = formatGreeting(props.name);
    const features = formatList(props.features);

    return createElement('div', { className: 'card mt-4' },
        createElement('div', { className: 'card-body' },
            createElement('h2', { className: 'card-title' }, greeting),
            createElement('p', { className: 'card-text' },
                'This component uses ES module imports. Features: ' + features + '.'
            ),
            createElement('div', { className: 'alert alert-info mt-3' },
                createElement('strong', null, 'How it works: '),
                'This component is written as an ES module with import/export syntax. ',
                'The server-side renderer automatically detects the module syntax and evaluates it ',
                'using V8\'s native ES module support, resolving all imports from embedded resources.'
            )
        )
    );
}
