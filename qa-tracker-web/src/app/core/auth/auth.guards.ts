import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Role } from '../../shared/models/role';
import { AuthService } from './auth.service';

/** Signed in with a changed password; otherwise to /login (with returnUrl) or /change-password. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.isAuthenticated()) {
    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  }
  if (auth.mustChangePassword()) {
    return router.createUrlTree(['/change-password'], { queryParams: { returnUrl: state.url } });
  }
  return true;
};

/** Route `data.roles` (Role[]) – users without one of the roles go Home. */
export const roleGuard: CanActivateFn = (route) => {
  const roles = route.data['roles'] as readonly Role[] | undefined;
  return !roles || inject(AuthService).hasRole(...roles) || inject(Router).createUrlTree(['/home']);
};

/** Any signed-in user (also one who must change a temporary password). */
export const signedInGuard: CanActivateFn = (_route, state) =>
  inject(AuthService).isAuthenticated() ||
  inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });

/** Login page: already signed-in users go to Home. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return (
    !auth.isAuthenticated() ||
    inject(Router).createUrlTree([auth.mustChangePassword() ? '/change-password' : '/home'])
  );
};
