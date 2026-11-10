import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AuthService } from './auth.service';

@Component({
  selector: 'app-login-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="login">
      <section class="card">
        <h1>OneSpace</h1>
        <p>Your front door to leave, policies and your calendar — in one question.</p>
        <button type="button" class="primary" (click)="signIn()" [disabled]="busy()">
          {{ busy() ? 'Redirecting…' : 'Sign in with Microsoft' }}
        </button>
      </section>
    </main>
  `,
  styles: `
    .login { min-height: 100dvh; display: grid; place-items: center; padding: 16px; }
    .card { max-width: 420px; width: 100%; padding: 32px; border-radius: 16px;
      background: var(--surface); border: 1px solid var(--border); text-align: center; }
    h1 { margin: 0 0 8px; font-size: 1.75rem; }
    p { color: var(--text-muted); margin: 0 0 24px; }
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  protected readonly busy = signal(false);

  protected async signIn(): Promise<void> {
    this.busy.set(true);
    try {
      await this.auth.signIn();
    } catch {
      this.busy.set(false);
    }
  }
}
