// The app's own origin, where the web server proxies the API, unless the build names another.
const API_URL = import.meta.env.VITE_API_URL || '/'

export const getApiUrl = () => API_URL
