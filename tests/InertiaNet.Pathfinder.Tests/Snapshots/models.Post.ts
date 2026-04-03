import type { User } from './User'

export interface Post {
    title: string
    author: User
    reviewers: User[]
}
