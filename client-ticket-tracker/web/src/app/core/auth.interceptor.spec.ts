import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let router: Router;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(), // replaces the real backend with a fake one we control
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => controller.verify());

  it('sends the user to /login when an API call returns 401', () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const clear = vi.spyOn(auth, 'clear');

    http.get('/api/tickets').subscribe({ error: () => undefined });
    controller.expectOne('/api/tickets').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(clear).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/' } });
  });

  it('does not redirect for /api/auth/* (a failed login must show its own error)', () => {
    const navigate = vi.spyOn(router, 'navigate');

    http.post('/api/auth/login', {}).subscribe({ error: () => undefined });
    controller.expectOne('/api/auth/login').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(navigate).not.toHaveBeenCalled();
  });
});
