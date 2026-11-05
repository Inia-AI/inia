export interface User {
  username: string | undefined
  fullName: string | undefined
  id: string
  email: string
}

export interface UserTenant {
  id: string
  name: string
  capabilities?: string[]
}

export interface ResolvedUser {
  username: string
  fullName: string | null
}

export interface ResolveUsersRequest {
  usernames: string[]
}
