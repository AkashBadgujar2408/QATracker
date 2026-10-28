import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatLabel, MatPrefix } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { ActivatedRoute, Router } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBuilding2,
  lucideChevronRight,
  lucideGlobe,
  lucideLayers,
  lucidePlus,
  lucideSearch,
  lucideUsers,
} from '@ng-icons/lucide';
import { problemMessage } from '../../core/api/api';
import { ToastService } from '../../core/feedback/toast.service';
import { Client, MastersApi, Project } from '../../shared/data/masters.api';
import { MasterDialogs } from '../../shared/masters/master-dialogs.service';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';
import { ClientPanel } from './client-panel';
import { PROJECT_TABS, ProjectPanel, ProjectTab } from './project-panel';

interface Selection {
  readonly clientId: number | null;
  readonly projectId: number | null;
  readonly tab: ProjectTab;
}

interface ProjectNodeChild {
  readonly tab: ProjectTab;
  readonly label: string;
  readonly icon: string;
  readonly count: (project: Project) => number;
}

const PROJECT_CHILDREN: readonly ProjectNodeChild[] = [
  { tab: 'modules', label: 'Modules', icon: 'lucideLayers', count: (p) => p.moduleCount },
  {
    tab: 'environments',
    label: 'Environments',
    icon: 'lucideGlobe',
    count: (p) => p.environmentCount,
  },
  { tab: 'members', label: 'Members', icon: 'lucideUsers', count: (p) => p.memberCount },
];

function idParam(value: string | null): number | null {
  const id = Number(value);
  return Number.isInteger(id) && id > 0 ? id : null;
}

/** Masters (plan 8.2): Clients → Projects → Modules / Environments / Members. Selection lives in the URL. */
@Component({
  selector: 'app-masters-page',
  imports: [
    MatButton,
    MatFormField,
    MatLabel,
    MatPrefix,
    MatInput,
    MatSlideToggle,
    NgIcon,
    PageHeader,
    EmptyState,
    SkeletonLines,
    ClientPanel,
    ProjectPanel,
  ],
  viewProviders: [
    provideIcons({
      lucideBuilding2,
      lucideChevronRight,
      lucideGlobe,
      lucideLayers,
      lucidePlus,
      lucideSearch,
      lucideUsers,
    }),
  ],
  templateUrl: './masters-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MastersPage {
  private readonly api = inject(MastersApi);
  private readonly dialogs = inject(MasterDialogs);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly projectChildren = PROJECT_CHILDREN;
  protected readonly search = signal('');
  protected readonly showInactive = signal(false);

  private readonly queryParams = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  protected readonly selection = computed<Selection>(() => {
    const params = this.queryParams();
    const tab = params.get('tab') as ProjectTab | null;
    return {
      clientId: idParam(params.get('client')),
      projectId: idParam(params.get('project')),
      tab: tab && PROJECT_TABS.includes(tab) ? tab : 'modules',
    };
  });

  protected readonly clients = rxResource({
    params: () => this.showInactive(),
    stream: ({ params }) => this.api.clients(params),
  });

  /** Projects per expanded client (loaded on first expand). */
  protected readonly projects = signal<ReadonlyMap<number, readonly Project[]>>(new Map());
  protected readonly expanded = signal<ReadonlySet<number>>(new Set());

  protected readonly visibleClients = computed(() => {
    const query = this.search().trim().toLowerCase();
    const clients = this.clients.hasValue() ? this.clients.value() : [];
    if (!query) {
      return clients;
    }
    return clients.filter(
      (client) =>
        client.name.toLowerCase().includes(query) ||
        (this.projects().get(client.id) ?? []).some(
          (p) => p.name.toLowerCase().includes(query) || p.key.toLowerCase().includes(query),
        ),
    );
  });

  protected readonly errorMessage = computed(() => problemMessage(this.clients.error()));

  constructor() {
    // Open the selected client's branch (also on a deep link).
    effect(() => {
      const clientId = this.selection().clientId;
      if (clientId && !untracked(this.expanded).has(clientId)) {
        untracked(() => this.toggle(clientId));
      }
    });
  }

  protected projectsOf(clientId: number): readonly Project[] | undefined {
    return this.projects().get(clientId);
  }

  protected toggle(clientId: number): void {
    const expanded = new Set(this.expanded());
    if (expanded.has(clientId)) {
      expanded.delete(clientId);
    } else {
      expanded.add(clientId);
      this.loadProjects(clientId);
    }
    this.expanded.set(expanded);
  }

  protected selectClient(client: Client): void {
    this.select({ client: client.id });
  }

  protected selectProject(project: Project, tab?: ProjectTab): void {
    this.select({ client: project.clientId, project: project.id, tab: tab ?? null });
  }

  protected openProject(clientId: number, projectId: number): void {
    this.select({ client: clientId, project: projectId });
    this.loadProjects(clientId);
  }

  protected setTab(tab: ProjectTab): void {
    const { clientId, projectId } = this.selection();
    this.select({ client: clientId, project: projectId, tab });
  }

  protected setShowInactive(show: boolean): void {
    this.showInactive.set(show);
    this.projects.set(new Map());
    for (const clientId of this.expanded()) {
      this.loadProjects(clientId);
    }
  }

  protected async addClient(): Promise<void> {
    const result = await this.dialogs.open({ kind: 'Client', name: this.search().trim() });
    if (!result) {
      return;
    }
    if (result.isNew) {
      this.toast.success(`Client ${result.name} added.`);
    }
    this.search.set('');
    this.clients.reload();
    this.select({ client: result.id });
  }

  /** A panel saved something: refresh the tree around it. */
  protected refresh(clientId: number | null): void {
    this.clients.reload();
    if (clientId) {
      this.loadProjects(clientId);
    }
  }

  private loadProjects(clientId: number): void {
    this.api.projects(clientId, this.showInactive()).subscribe({
      next: (projects) => {
        const map = new Map(this.projects());
        map.set(clientId, projects);
        this.projects.set(map);
      },
      error: (error: unknown) => this.toast.error(problemMessage(error)),
    });
  }

  private select(params: {
    client: number | null;
    project?: number | null;
    tab?: ProjectTab | null;
  }): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        client: params.client,
        project: params.project ?? null,
        tab: params.tab && params.tab !== 'modules' ? params.tab : null,
      },
      replaceUrl: true,
    });
  }
}
