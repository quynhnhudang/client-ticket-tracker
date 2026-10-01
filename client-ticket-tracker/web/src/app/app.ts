import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';

/** The app shell: top bar (when logged in) plus the routed page. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, FormsModule],
  templateUrl: './app.html',
})
export class App {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected searchText = '';

  protected search(): void {
    const q = this.searchText.trim();
    if (q) void this.router.navigate(['/search'], { queryParams: { q } });
  }

  protected logout(): void {
    this.auth.logout().subscribe(() => void this.router.navigate(['/login']));
  }
}
