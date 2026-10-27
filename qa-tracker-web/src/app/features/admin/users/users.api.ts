import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL, SILENT_ERRORS } from '../../../core/api/api';
import { Role } from '../../../shared/models/role';

/** /api/users DTOs (plan 14, ADR-013). The password is never part of these. */
export interface User {
  readonly id: number;
  readonly userName: string;
  readonly fullName: string;
  readonly email: string;
  readonly role: Role;
  readonly isActive: boolean;
  readonly mustChangePassword: boolean;
  readonly avatarColor: string | null;
  readonly lastLoginAt: string | null;
  readonly createdAt: string;
  readonly rowVersion: string;
}

export interface UserWithPassword {
  readonly user: User;
  readonly temporaryPassword: string;
}

export interface UserFilter {
  readonly search?: string;
  readonly role?: Role | null;
  readonly isActive?: boolean | null;
}

export interface CreateUser {
  readonly userName: string;
  readonly fullName: string;
  readonly email: string;
  readonly role: Role;
}

export interface UpdateUser {
  readonly fullName: string;
  readonly email: string;
  readonly role: Role;
  readonly avatarColor: string | null;
  readonly rowVersion: string;
}

/** Form submissions show errors inline, so they opt out of the global error toast. */
const inline = { context: new HttpContext().set(SILENT_ERRORS, true) };

@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);
  private readonly url = `${inject(API_BASE_URL)}/users`;

  list(filter: UserFilter): Observable<User[]> {
    let params = new HttpParams();
    if (filter.search?.trim()) {
      params = params.set('search', filter.search.trim());
    }
    if (filter.role) {
      params = params.set('role', filter.role);
    }
    if (filter.isActive !== null && filter.isActive !== undefined) {
      params = params.set('isActive', filter.isActive);
    }
    return this.http.get<User[]>(this.url, { params });
  }

  create(user: CreateUser): Observable<UserWithPassword> {
    return this.http.post<UserWithPassword>(this.url, user, inline);
  }

  update(id: number, user: UpdateUser): Observable<User> {
    return this.http.put<User>(`${this.url}/${id}`, user, inline);
  }

  resetPassword(id: number): Observable<UserWithPassword> {
    return this.http.post<UserWithPassword>(`${this.url}/${id}/reset-password`, null);
  }

  setActive(id: number, isActive: boolean): Observable<User> {
    return this.http.patch<User>(`${this.url}/${id}/active`, { isActive }, inline);
  }
}
