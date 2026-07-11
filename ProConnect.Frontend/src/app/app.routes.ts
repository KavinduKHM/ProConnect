import { Routes } from '@angular/router';
import { LoginComponent } from './features/auth/login/login.component';
import { RegisterComponent } from './features/auth/register/register.component';

export const routes: Routes = [
  { path: '', redirectTo: '/jobs', pathMatch: 'full' },
  { path: 'login', component: LoginComponent },
  { path: 'register', component: RegisterComponent },
//   { path: 'jobs', loadComponent: () => import('./features/jobs/job-list/job-list.component').then(m => m.JobListComponent) },
//   { path: 'jobs/create', loadComponent: () => import('./features/jobs/job-create/job-create.component').then(m => m.JobCreateComponent) },
//   { path: 'jobs/:id', loadComponent: () => import('./features/jobs/job-detail/job-detail.component').then(m => m.JobDetailComponent) },
  { path: '**', redirectTo: '/jobs' }
];