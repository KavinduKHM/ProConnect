import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent, HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  constructor(private authService: AuthService, private router: Router) {}

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    const isAuthEndpoint = req.url.includes('/Auth/login') || req.url.includes('/Auth/register');
    const token = this.authService.getToken();

    if (token && !isAuthEndpoint) {
      req = req.clone({
        setHeaders: {
          Authorization: `Bearer ${token}`
        }
      });
    }

    return next.handle(req).pipe(
      catchError((error: HttpErrorResponse) => {
        // The token was rejected (expired or invalid): drop the session and send
        // the user to login instead of letting every screen keep retrying with it.
        if (error.status === 401 && !isAuthEndpoint) {
          const returnUrl = this.router.url;
          this.authService.logout();
          this.router.navigate(['/login'], { queryParams: { sessionExpired: true, returnUrl } });
        }
        return throwError(() => error);
      })
    );
  }
}
