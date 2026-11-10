import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in · OneSpace',
    loadComponent: () => import('./core/auth/login.page').then(m => m.LoginPage),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell').then(m => m.Shell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'assistant' },
      // Each feature is self-contained and lazy-loaded (Phase 1: feed, helpdesk, idea-hub follow the same shape).
      { path: 'assistant', loadChildren: () => import('./features/assistant/assistant.routes').then(m => m.assistantRoutes) },
    ],
  },
  { path: '**', redirectTo: '' },
];
