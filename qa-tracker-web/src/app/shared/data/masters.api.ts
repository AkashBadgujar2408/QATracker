import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL, SILENT_ERRORS } from '../../core/api/api';
import { Role } from '../models/role';

/** Masters DTOs (plan 14 CLIENTS / PROJECTS / LOOKUPS, ADR-015). */
export interface Client {
  readonly id: number;
  readonly name: string;
  readonly contactPerson: string | null;
  readonly email: string | null;
  readonly phone: string | null;
  readonly isActive: boolean;
  readonly projectCount: number;
  readonly rowVersion: string;
}

export interface Project {
  readonly id: number;
  readonly clientId: number;
  readonly clientName: string;
  readonly name: string;
  readonly key: string;
  readonly requireLiveVerification: boolean;
  readonly isActive: boolean;
  readonly moduleCount: number;
  readonly environmentCount: number;
  readonly memberCount: number;
  /** The key is locked once issues exist. */
  readonly hasIssues: boolean;
  readonly rowVersion: string;
}

export interface Module {
  readonly id: number;
  readonly projectId: number;
  readonly name: string;
  readonly isActive: boolean;
  readonly rowVersion: string;
}

export type EnvironmentType = 'UAT' | 'LIVE' | 'Other';
export const ENVIRONMENT_TYPES: readonly EnvironmentType[] = ['UAT', 'LIVE', 'Other'];

export interface ProjectEnvironment {
  readonly id: number;
  readonly projectId: number;
  readonly name: string;
  readonly type: EnvironmentType;
  readonly url: string;
  readonly isActive: boolean;
  readonly rowVersion: string;
}

export interface Member {
  readonly userId: number;
  readonly userName: string;
  readonly fullName: string;
  readonly role: Role;
  readonly avatarColor: string | null;
  readonly isActive: boolean;
}

export interface UserLookup {
  readonly id: number;
  readonly userName: string;
  readonly fullName: string;
  readonly role: Role;
  readonly avatarColor: string | null;
}

export interface LookupItem {
  readonly id: number;
  readonly name: string;
}

export interface Cascade {
  readonly clients: readonly LookupItem[];
  readonly projects: readonly (LookupItem & { readonly key: string })[];
  readonly modules: readonly LookupItem[];
  readonly environments: readonly (LookupItem & {
    readonly type: EnvironmentType;
    readonly url: string;
  })[];
  readonly members: readonly {
    readonly userId: number;
    readonly fullName: string;
    readonly role: Role;
  }[];
}

export type MasterKind = 'Client' | 'Project' | 'Module' | 'Environment';

export interface SimilarName {
  readonly id: number;
  readonly name: string;
  /** Same name ignoring case, spaces and punctuation – creating it would be a duplicate. */
  readonly exact: boolean;
  readonly isActive: boolean;
}

export interface ClientFields {
  readonly name: string;
  readonly contactPerson?: string | null;
  readonly email?: string | null;
  readonly phone?: string | null;
}

export interface ProjectFields {
  readonly name: string;
  readonly key: string;
  readonly requireLiveVerification: boolean;
}

export interface EnvironmentFields {
  readonly name: string;
  readonly type: EnvironmentType;
  readonly url: string;
}

type WithRowVersion<T> = T & { readonly rowVersion: string };

/** Form submissions show errors inline, so they opt out of the global error toast. */
const inline = { context: new HttpContext().set(SILENT_ERRORS, true) };

function withInactive(includeInactive: boolean): { params: HttpParams } {
  return { params: new HttpParams().set('includeInactive', includeInactive) };
}

@Injectable({ providedIn: 'root' })
export class MastersApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  clients(includeInactive = false): Observable<Client[]> {
    return this.http.get<Client[]>(`${this.base}/clients`, withInactive(includeInactive));
  }

  client(id: number): Observable<Client> {
    return this.http.get<Client>(`${this.base}/clients/${id}`);
  }

  createClient(client: ClientFields): Observable<Client> {
    return this.http.post<Client>(`${this.base}/clients`, client, inline);
  }

  updateClient(id: number, client: WithRowVersion<ClientFields>): Observable<Client> {
    return this.http.put<Client>(`${this.base}/clients/${id}`, client, inline);
  }

  setClientActive(id: number, isActive: boolean): Observable<Client> {
    return this.http.patch<Client>(`${this.base}/clients/${id}/active`, { isActive });
  }

  projects(clientId: number, includeInactive = false): Observable<Project[]> {
    return this.http.get<Project[]>(
      `${this.base}/clients/${clientId}/projects`,
      withInactive(includeInactive),
    );
  }

  project(id: number): Observable<Project> {
    return this.http.get<Project>(`${this.base}/projects/${id}`);
  }

  createProject(clientId: number, project: ProjectFields): Observable<Project> {
    return this.http.post<Project>(`${this.base}/clients/${clientId}/projects`, project, inline);
  }

  updateProject(id: number, project: WithRowVersion<ProjectFields>): Observable<Project> {
    return this.http.put<Project>(`${this.base}/projects/${id}`, project, inline);
  }

  setProjectActive(id: number, isActive: boolean): Observable<Project> {
    return this.http.patch<Project>(`${this.base}/projects/${id}/active`, { isActive });
  }

  modules(projectId: number, includeInactive = false): Observable<Module[]> {
    return this.http.get<Module[]>(
      `${this.base}/projects/${projectId}/modules`,
      withInactive(includeInactive),
    );
  }

  createModule(projectId: number, name: string): Observable<Module> {
    return this.http.post<Module>(`${this.base}/projects/${projectId}/modules`, { name }, inline);
  }

  updateModule(id: number, module: WithRowVersion<{ readonly name: string }>): Observable<Module> {
    return this.http.put<Module>(`${this.base}/modules/${id}`, module, inline);
  }

  setModuleActive(id: number, isActive: boolean): Observable<Module> {
    return this.http.patch<Module>(`${this.base}/modules/${id}/active`, { isActive });
  }

  environments(projectId: number, includeInactive = false): Observable<ProjectEnvironment[]> {
    return this.http.get<ProjectEnvironment[]>(
      `${this.base}/projects/${projectId}/environments`,
      withInactive(includeInactive),
    );
  }

  createEnvironment(
    projectId: number,
    environment: EnvironmentFields,
  ): Observable<ProjectEnvironment> {
    return this.http.post<ProjectEnvironment>(
      `${this.base}/projects/${projectId}/environments`,
      environment,
      inline,
    );
  }

  updateEnvironment(
    id: number,
    environment: WithRowVersion<EnvironmentFields>,
  ): Observable<ProjectEnvironment> {
    return this.http.put<ProjectEnvironment>(
      `${this.base}/environments/${id}`,
      environment,
      inline,
    );
  }

  setEnvironmentActive(id: number, isActive: boolean): Observable<ProjectEnvironment> {
    return this.http.patch<ProjectEnvironment>(`${this.base}/environments/${id}/active`, {
      isActive,
    });
  }

  members(projectId: number): Observable<Member[]> {
    return this.http.get<Member[]>(`${this.base}/projects/${projectId}/members`);
  }

  setMembers(projectId: number, userIds: readonly number[]): Observable<Member[]> {
    return this.http.put<Member[]>(`${this.base}/projects/${projectId}/members`, { userIds });
  }

  cascade(clientId?: number | null, projectId?: number | null): Observable<Cascade> {
    let params = new HttpParams();
    if (clientId) {
      params = params.set('clientId', clientId);
    }
    if (projectId) {
      params = params.set('projectId', projectId);
    }
    return this.http.get<Cascade>(`${this.base}/lookups/cascade`, { params });
  }

  /** "Did you mean…?" – parentId is the client for projects, the project for modules/environments. */
  similar(kind: MasterKind, name: string, parentId?: number | null): Observable<SimilarName[]> {
    let params = new HttpParams().set('kind', kind).set('name', name);
    if (parentId) {
      params = params.set('parentId', parentId);
    }
    return this.http.get<SimilarName[]>(`${this.base}/lookups/similar`, { params, ...inline });
  }

  suggestProjectKey(clientId: number, name: string): Observable<{ key: string }> {
    const params = new HttpParams().set('clientId', clientId).set('name', name);
    return this.http.get<{ key: string }>(`${this.base}/lookups/project-key`, {
      params,
      ...inline,
    });
  }

  users(role?: Role): Observable<UserLookup[]> {
    const params = role ? new HttpParams().set('role', role) : new HttpParams();
    return this.http.get<UserLookup[]>(`${this.base}/lookups/users`, { params });
  }
}
