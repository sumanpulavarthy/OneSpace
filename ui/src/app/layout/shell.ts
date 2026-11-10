import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { RuntimeConfigService } from '../core/config/runtime-config.service';

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="topbar">
      <a class="brand" routerLink="/">One<span>Space</span></a>
      <nav aria-label="Main">
        @for (item of runtime.menu(); track item.route) {
          <a [routerLink]="item.route" routerLinkActive="active">{{ item.label }}</a>
        }
      </nav>
      <div class="user">
        @if (auth.configured()) {
          <span class="name">{{ auth.user()?.name }}</span>
          <button type="button" class="ghost" (click)="auth.signOut()">Sign out</button>
        } @else {
          <span class="dev" title="No Entra ID config in app-config.json; the API's development user is used.">Dev mode</span>
        }
      </div>
    </header>
    <main class="content"><router-outlet /></main>
  `,
  styles: `
    :host { display: grid; grid-template-rows: auto 1fr; height: 100dvh; }
    .topbar { display: flex; align-items: center; gap: 24px; padding: 12px 16px;
      border-bottom: 1px solid var(--border); background: var(--surface); }
    .brand { font-weight: 700; font-size: 1.125rem; color: var(--text); text-decoration: none; }
    .brand span { color: var(--accent); }
    nav { display: flex; gap: 16px; flex: 1; }
    nav a { color: var(--text-muted); text-decoration: none; font-size: 0.875rem; }
    nav a.active { color: var(--text); font-weight: 600; }
    .user { display: flex; align-items: center; gap: 12px; font-size: 0.875rem; min-width: 0; }
    .name { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 30vw; }
    .dev { font-size: 0.75rem; padding: 2px 8px; border-radius: 999px; background: var(--chip); color: var(--text-muted); }
    .content { min-height: 0; }
  `,
})
export class Shell {
  protected readonly auth = inject(AuthService);
  protected readonly runtime = inject(RuntimeConfigService);
}
