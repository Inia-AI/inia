const GRANT_TYPE = import.meta.env.VITE_OAUTH_GRANT_TYPE || 'password'
const SCOPE = import.meta.env.VITE_OAUTH_SCOPE || 'offline_access api'

export const getGrantType = () => GRANT_TYPE

export const getScope = () => SCOPE
