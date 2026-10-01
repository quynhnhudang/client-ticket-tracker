import { Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { safeReturnUrl } from '../../core/auth.guard';
import { AuthService } from '../../core/auth.service';
import { problemMessage } from '../../core/problem';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  template: `
    <section class="card narrow">
      <h1>Sign in</h1>
      <p class="muted">Support agents only.</p>

      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <label for="email">Email</label>
        <input id="email" type="email" formControlName="email" autocomplete="username" />

        <label for="password">Password</label>
        <input id="password" type="password" formControlName="password" autocomplete="current-password" />

        @if (error()) {
          <p class="error" role="alert">{{ error() }}</p>
        }

        <button type="submit" class="primary" [disabled]="form.invalid || busy()">
          {{ busy() ? 'Signing in…' : 'Sign in' }}
        </button>
      </form>
    </section>
  `,
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** From ?returnUrl=... (set by the auth guard). Bound via withComponentInputBinding. */
  readonly returnUrl = input<string>();

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  protected submit(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set(null);

    const { email, password } = this.form.getRawValue();
    this.auth.login(email, password).subscribe({
      next: () => void this.router.navigateByUrl(safeReturnUrl(this.returnUrl())),
      error: (err: unknown) => {
        this.error.set(problemMessage(err));
        this.busy.set(false);
      },
    });
  }
}
