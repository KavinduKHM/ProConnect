import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { JobService, Job } from '../../../core/services/job.service';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-job-list',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatChipsModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './job-list.component.html',
  styleUrls: ['./job-list.component.css']
})
export class JobListComponent implements OnInit {
  jobs: Job[] = [];
  isLoading = true;
  errorMessage: string | null = null;

  constructor(
    private jobService: JobService,
    public authService: AuthService
  ) {}

  ngOnInit(): void {
    this.loadJobs();
  }

  loadJobs(): void {
    this.isLoading = true;
    this.jobService.getJobs().subscribe({
      next: (data) => {
        this.jobs = data;
        this.isLoading = false;
      },
      error: (err) => {
        this.errorMessage = 'Failed to load jobs. Please try again.';
        this.isLoading = false;
        console.error(err);
      }
    });
  }

  getStatusColor(status: string): string {
    switch (status) {
      case 'Open': return 'primary';
      case 'Bidding': return 'accent';
      case 'Assigned': return 'warn';
      case 'Completed': return 'success';
      case 'Cancelled': return 'warn';
      default: return '';
    }
  }
}