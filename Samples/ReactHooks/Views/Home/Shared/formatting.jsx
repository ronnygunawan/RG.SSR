// Shared utility module - demonstrates transitive ES module imports
// JSX source version (no JSX syntax here, but kept alongside for consistency)
export function formatGreeting(name) {
    return 'Hello, ' + name + '!';
}

export function formatDate(date) {
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
                    'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    return months[date.getMonth()] + ' ' + date.getDate() + ', ' + date.getFullYear();
}

export function formatList(items) {
    if (items.length === 0) return 'none';
    if (items.length === 1) return items[0];
    return items.slice(0, -1).join(', ') + ' and ' + items[items.length - 1];
}
