import { Role } from '../../shared/models/role';

/** GET /api/auth/me – the signed-in user. */
export interface CurrentUser {
  readonly id: number;
  readonly userName: string;
  readonly fullName: string;
  readonly email: string;
  readonly role: Role;
  readonly mustChangePassword: boolean;
  readonly avatarColor: string | null;
}

/** Response of login / refresh / change-password (the refresh token travels only in the httpOnly cookie). */
export interface AuthResponse {
  readonly accessToken: string;
  readonly accessTokenExpiresAt: string;
  readonly user: CurrentUser;
}
