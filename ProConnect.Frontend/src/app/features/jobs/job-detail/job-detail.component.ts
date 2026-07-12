import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDividerModule } from '@angular/material/divider';
import { JobService, Job, Bid } from '../../../core/services/job.service';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-job-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    ReactiveFormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    MatFormFieldModule,
    MatInputModule,
    MatDividerModule
  ],
  templateUrl: './job-detail.component.html',
  styleUrls: ['./job-detail.component.css']
})
export class JobDetailComponent implements OnInit {
  job: Job | null = null;
  bids: Bid[] = [];
  isLoading = true;
  isBidding = false;
  errorMessage: string | null = null;
  bidForm: FormGroup;
  showBidForm = false;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private jobService: JobService,
    private authService: AuthService,
    private fb: FormBuilder
  ) {
    this.bidForm = this.fb.group({
      bidAmount: ['', [Validators.required, Validators.min(1)]],
      proposalMessage: ['', [Validators.required, Validators.minLength(10)]],
      estimatedDays: ['', [Validators.required, Validators.min(1)]]
    });
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.loadJob(+id);
    } else {
      this.router.navigate(['/jobs']);
    }
  }

  loadJob(id: number): void {
    this.isLoading = true;
    this.jobService.getJob(id).subscribe({
      next: (job) => {
        this.job = job;
        this.isLoading = false;
        if (this.isJobOwner()) {
          this.loadBids(id);
        }
      },
      error: (err) => {
        this.errorMessage = 'Failed to load job details.';
        this.isLoading = false;
        console.error(err);
      }
    });
  }

  loadBids(jobId: number): void {
    this.jobService.getBids(jobId).subscribe({
      next: (bids) => {
        this.bids = bids;
      },
      error: (err) => {
        console.error('Failed to load bids', err);
      }
    });
  }

  isJobOwner(): boolean {
    if (!this.job) return false;
    const user = this.authService.getCurrentUser();
    return user?.fullName === this.job.customerName;
  }

  canPlaceBid(): boolean {
    const hasRole = this.authService.hasRole('Vendor');
    const isOpen = this.job?.status === 'Open';
    const isOwner = this.isJobOwner();
    const result = hasRole && isOpen && !isOwner;
    
    console.log('canPlaceBid debug:', {
      hasRole,
      isOpen,
      isOwner,
      result,
      jobStatus: this.job?.status,
      userRole: this.authService.getUserRole(),
      jobCustomerName: this.job?.customerName,
      currentUser: this.authService.getCurrentUser()?.fullName
    });
    
    return result;
  }

  toggleBidForm(): void {
    this.showBidForm = !this.showBidForm;
    if (!this.showBidForm) {
      this.bidForm.reset();
    }
  }

  submitBid(): void {
    if (this.bidForm.invalid || !this.job) {
      this.bidForm.markAllAsTouched();
      return;
    }

    this.isBidding = true;
    this.errorMessage = null;

    const payload = {
      bidAmount: +this.bidForm.value.bidAmount,
      proposalMessage: this.bidForm.value.proposalMessage,
      estimatedDays: +this.bidForm.value.estimatedDays
    };

    this.jobService.placeBid(this.job.id, payload).subscribe({
      next: () => {
        this.isBidding = false;
        this.showBidForm = false;
        this.bidForm.reset();
        // Reload job and bids
        this.loadJob(this.job!.id);
      },
      error: (err) => {
        this.isBidding = false;
        this.errorMessage = err.error?.message || 'Failed to place bid.';
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