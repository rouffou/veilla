import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { RUNTIME_CONFIG } from '../config/runtime-config';
import { apiAuthInterceptor, isApiUrl } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('isApiUrl', () => {
  const base = 'https://app.example.test/';

  it("reconnaît les URL sous la base de l'API", () => {
    expect(isApiUrl('https://api.example.test/bff/affilies', 'https://api.example.test/bff', base)).toBe(true);
    expect(isApiUrl('https://api.example.test/bff', 'https://api.example.test/bff', base)).toBe(true);
    expect(isApiUrl('/api/x', '/api', base)).toBe(true);
  });

  it('rejette les autres origines et les préfixes trompeurs', () => {
    expect(isApiUrl('https://evil.example.test/bff/x', 'https://api.example.test/bff', base)).toBe(false);
    expect(isApiUrl('https://api.example.test/bff-autre/x', 'https://api.example.test/bff', base)).toBe(false);
    expect(isApiUrl('i18n/fr.json', '/api', base)).toBe(false);
  });
});

describe('apiAuthInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiAuthInterceptor])),
        provideHttpClientTesting(),
        {
          provide: RUNTIME_CONFIG,
          useValue: {
            apiBaseUrl: 'https://api.example.test/bff',
            auth: { authority: 'https://idp.example.test', clientId: 'test', scope: 'openid' },
          },
        },
        { provide: AuthService, useValue: { accessToken: () => of('jeton-de-test') } },
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('ajoute le jeton vers le BFF', () => {
    http.get('https://api.example.test/bff/me').subscribe();
    const req = controller.expectOne('https://api.example.test/bff/me');
    expect(req.request.headers.get('Authorization')).toBe('Bearer jeton-de-test');
    req.flush({});
  });

  it("n'ajoute pas le jeton vers d'autres destinations", () => {
    http.get('https://cdn.example.test/file.json').subscribe();
    const req = controller.expectOne('https://cdn.example.test/file.json');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });
});
