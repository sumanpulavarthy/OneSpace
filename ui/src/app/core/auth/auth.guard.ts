import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Requires a signed-in employee. In local dev mode (no Entra ID config) everyone passes. */
export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  if (!auth.configured()) return true;
  return (await auth.hasValidSession()) ? true : inject(Router).parseUrl('/login');
};
