export interface UserPermission {
  id: number
  userId: string
  userName: string
  userEmail: string
  departmentName: string
  connectionIds: number[]
  connectionNames: string[]
  permissionScopeText: string
  isActive: boolean
  createdAt: string
}

export interface DepartmentPermission {
  id: number
  departmentId: string
  departmentName: string
  description: string
  memberCount: number
  connectionIds: number[]
  connectionNames: string[]
  isActive: boolean
  createdAt: string
}

export interface UserPermissionDashboard {
  totalUsers: number
  usersWithAccess: number
  totalConnections: number
  activePermissions: number
  userPermissions: UserPermission[]
}

export interface DepartmentPermissionDashboard {
  totalDepartments: number
  departmentsWithAccess: number
  totalConnections: number
  activePermissions: number
  departmentPermissions: DepartmentPermission[]
}

export interface AssignUserPermissionPayload {
  userId: string
  userName: string
  userEmail: string
  departmentName: string
  connectionIds: number[]
}

export interface AssignDepartmentPermissionPayload {
  departmentId: string
  departmentName: string
  description: string
  memberCount: number
  connectionIds: number[]
}
