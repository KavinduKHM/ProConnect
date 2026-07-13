import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDividerModule } from '@angular/material/divider';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { JobService, Job, Bid } from '../../../core/services/job.service';
import { BookingService, Booking } from '../../../core/services/booking.service';
import { ReviewService, Review } from '../../../core/services/review.service';
import { AiService, BidEvaluation, VendorMatch, BidRanking } from '../../../core/services/ai.service';
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
    MatDividerModule,
    MatSnackBarModule
  ],
  templateUrl: './job-detail.component.html',
  styleUrls: ['./job-detail.component.css']
})
export class JobDetailComponent implements OnInit {
  job: Job | null = null;
  bids: Bid[] = [];
  booking: Booking | null = null;
  review: Review | null = null;

  isLoading = true;
  isBidding = false;
  isEvaluating = false;
  isWorkingBooking = false;
  isSubmittingReview = false;
  errorMessage: string | null = null;

  bidForm: FormGroup;
  reviewForm: FormGroup;
  showBidForm = false;
  bidEvaluation: BidEvaluation | null = null;

  // AI: recommended vendors (customer)
  recommendedVendors: VendorMatch[] = [];
  isMatching = false;

  // AI: bid ranking (customer)
  bidRanking: BidRanking | null = null;
  isRanking = false;

  // AI: proposal writer (vendor)
  isWritingProposal = false;

  // Proof-of-completion photo (vendor)
  completionPhoto: File | null = null;
  completionPreview: string | null = null;

  readonly stars = [1, 2, 3, 4, 5];

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private jobService: JobService,
    private bookingService: BookingService,
    private reviewService: ReviewService,
    private aiService: AiService,
    private authService: AuthService,
    private snackBar: MatSnackBar,
    private fb: FormBuilder
  ) {
    this.bidForm = this.fb.group({
      bidAmount: ['', [Validators.required, Validators.min(1)]],
      proposalMessage: ['', [Validators.required, Validators.minLength(10)]],
      estimatedDays: ['', [Validators.required, Validators.min(1)]]
    });

    this.reviewForm = this.fb.group({
      rating: [0, [Validators.required, Validators.min(1), Validators.max(5)]],
      comment: ['']
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

        if (job.isOwner) {
          this.loadBids(id);
        }
        if (job.bookingId) {
          this.loadBooking(job.bookingId);
        }
        if (job.hasReview) {
          this.loadReview(id);
        }
      },
      error: (err) => {
        this.errorMessage = 'Failed to load job details.';
        this.isLoading = false;
        console.error(err);
      }
    });
  }

  // ---------------------------------------------------------------- image

  get imageSrc(): string | null {
    return this.job ? this.jobService.imageUrl(this.job) : null;
  }

  /** The vendor's proof-of-completion photo, once uploaded. */
  get completionSrc(): string | null {
    return this.job ? this.jobService.completionImageUrl(this.job) : null;
  }

  // ---------------------------------------------------------------- bids

  loadBids(jobId: number): void {
    this.jobService.getBids(jobId).subscribe({
      next: (bids) => (this.bids = bids),
      error: (err) => console.error('Failed to load bids', err)
    });
  }

  canPlaceBid(): boolean {
    return this.authService.hasRole('Vendor') && this.job?.status === 'Open';
  }

  toggleBidForm(): void {
    this.showBidForm = !this.showBidForm;
    if (!this.showBidForm) {
      this.bidForm.reset();
      this.bidEvaluation = null;
    }
  }

  /** Ask the AI whether this price is sane before the vendor commits to it. */
  evaluateBid(): void {
    const amount = +this.bidForm.value.bidAmount;
    const days = +this.bidForm.value.estimatedDays || 1;
    if (!this.job || !amount) {
      this.snackBar.open('Enter a bid amount first.', 'Close', { duration: 3000 });
      return;
    }

    this.isEvaluating = true;
    this.bidEvaluation = null;
    this.aiService.evaluateBid(this.job.id, amount, days).subscribe({
      next: (evaluation) => {
        this.isEvaluating = false;
        this.bidEvaluation = evaluation;
      },
      error: (err) => {
        this.isEvaluating = false;
        this.snackBar.open(err.error?.message || 'Could not check this bid.', 'Close', { duration: 5000 });
      }
    });
  }

  verdictClass(verdict: string): string {
    switch (verdict) {
      case 'TooLow': return 'verdict-low';
      case 'TooHigh': return 'verdict-high';
      default: return 'verdict-fair';
    }
  }

  submitBid(): void {
    if (this.bidForm.invalid || !this.job) {
      this.bidForm.markAllAsTouched();
      return;
    }

    this.isBidding = true;
    this.errorMessage = null;

    this.jobService.placeBid(this.job.id, {
      bidAmount: +this.bidForm.value.bidAmount,
      proposalMessage: this.bidForm.value.proposalMessage,
      estimatedDays: +this.bidForm.value.estimatedDays
    }).subscribe({
      next: () => {
        this.isBidding = false;
        this.showBidForm = false;
        this.bidForm.reset();
        this.bidEvaluation = null;
        this.snackBar.open('Bid placed.', 'Close', { duration: 3000 });
        this.loadJob(this.job!.id);
      },
      error: (err) => {
        this.isBidding = false;
        this.errorMessage = err.error?.message || 'Failed to place bid.';
      }
    });
  }

  acceptBid(bidId: number): void {
    if (!this.job) return;
    if (!confirm('Accept this bid? The vendor will be assigned and the job booked.')) return;

    this.isLoading = true;
    this.jobService.acceptBid(this.job.id, bidId).subscribe({
      next: () => {
        this.snackBar.open('Bid accepted. The job is now booked.', 'Close', { duration: 4000 });
        this.loadJob(this.job!.id);
      },
      error: (err) => {
        this.isLoading = false;
        this.errorMessage = err.error?.message || 'Failed to accept bid.';
      }
    });
  }

  rejectBid(bidId: number): void {
    if (!this.job) return;
    if (!confirm('Reject this bid?')) return;

    this.isLoading = true;
    this.jobService.rejectBid(this.job.id, bidId).subscribe({
      next: () => this.loadJob(this.job!.id),
      error: (err) => {
        this.isLoading = false;
        this.errorMessage = err.error?.message || 'Failed to reject bid.';
      }
    });
  }

  /** The vendors the AI thinks fit this job. Customer-only. */
  findVendors(): void {
    if (!this.job) return;
    this.isMatching = true;
    this.recommendedVendors = [];
    this.jobService.getRecommendedVendors(this.job.id).subscribe({
      next: (matches) => {
        this.isMatching = false;
        this.recommendedVendors = matches;
        if (matches.length === 0) {
          this.snackBar.open('No vendors match this job yet.', 'Close', { duration: 4000 });
        }
      },
      error: (err) => {
        this.isMatching = false;
        this.snackBar.open(err.error?.message || 'Could not find matching vendors.', 'Close', { duration: 5000 });
      }
    });
  }

  /** Weighs the open bids on price, timeline, proposal quality and vendor reputation. */
  rankBids(): void {
    if (!this.job) return;
    this.isRanking = true;
    this.bidRanking = null;
    this.jobService.rankBids(this.job.id).subscribe({
      next: (ranking) => {
        this.isRanking = false;
        this.bidRanking = ranking;
      },
      error: (err) => {
        this.isRanking = false;
        this.snackBar.open(err.error?.message || 'Could not compare the bids.', 'Close', { duration: 5000 });
      }
    });
  }

  /** The AI's rank for a given bid, so the bid list can show it inline. */
  rankFor(bidId: number): number | null {
    return this.bidRanking?.ranking.find(r => r.bidId === bidId)?.rank ?? null;
  }

  reasonFor(bidId: number): string | null {
    return this.bidRanking?.ranking.find(r => r.bidId === bidId)?.reason ?? null;
  }

  /** Turns the vendor's rough notes into a proposal. */
  writeProposal(): void {
    const notes = this.bidForm.value.proposalMessage;
    if (!this.job || !notes || notes.trim().length < 5) {
      this.snackBar.open('Jot down a few notes first, then let the AI write it up.', 'Close', { duration: 4000 });
      return;
    }

    this.isWritingProposal = true;
    this.aiService.writeProposal(
      this.job.id,
      notes,
      +this.bidForm.value.bidAmount || undefined,
      +this.bidForm.value.estimatedDays || undefined
    ).subscribe({
      next: (result) => {
        this.isWritingProposal = false;
        this.bidForm.patchValue({ proposalMessage: result.proposal });
        this.snackBar.open('Proposal written by AI — edit it before you send.', 'Close', { duration: 4000 });
      },
      error: (err) => {
        this.isWritingProposal = false;
        this.snackBar.open(err.error?.message || 'Could not write the proposal.', 'Close', { duration: 5000 });
      }
    });
  }

  // ---------------------------------------------------------------- booking

  loadBooking(bookingId: number): void {
    this.bookingService.getBooking(bookingId).subscribe({
      next: (booking) => (this.booking = booking),
      error: (err) => console.error('Failed to load booking', err)
    });
  }

  canStartWork(): boolean {
    return !!this.job?.isAssignedVendor && this.booking?.status === 'Scheduled';
  }

  canCompleteWork(): boolean {
    return !!this.job?.isAssignedVendor &&
      (this.booking?.status === 'Scheduled' || this.booking?.status === 'InProgress');
  }

  canCancelBooking(): boolean {
    return !!this.booking &&
      this.booking.status !== 'Completed' &&
      this.booking.status !== 'Cancelled' &&
      (!!this.job?.isOwner || !!this.job?.isAssignedVendor);
  }

  startWork(): void {
    if (!this.booking) return;
    this.runBookingAction(this.bookingService.start(this.booking.id), 'Work started.');
  }

  onCompletionPhotoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files?.length) return;

    const file = input.files[0];
    if (!file.type.startsWith('image/')) {
      this.snackBar.open('The completion photo must be an image.', 'Close', { duration: 3000 });
      return;
    }
    if (file.size > 5_000_000) {
      this.snackBar.open('File size exceeds 5 MB.', 'Close', { duration: 3000 });
      return;
    }

    this.completionPhoto = file;
    const reader = new FileReader();
    reader.onload = () => (this.completionPreview = reader.result as string);
    reader.readAsDataURL(file);
  }

  completeWork(): void {
    if (!this.booking) return;
    if (!confirm('Mark this job as complete? The customer will be asked to review you.')) return;

    // The photo is optional; when given, the AI compares it against the customer's original.
    this.runBookingAction(
      this.bookingService.complete(this.booking.id, this.completionPhoto),
      'Job marked complete.'
    );
  }

  cancelBooking(): void {
    if (!this.booking) return;
    if (!confirm('Cancel this booking?')) return;
    this.runBookingAction(this.bookingService.cancel(this.booking.id), 'Booking cancelled.');
  }

  private runBookingAction(action: Observable<Booking>, successMessage: string): void {
    this.isWorkingBooking = true;
    action.subscribe({
      next: (booking) => {
        this.isWorkingBooking = false;
        this.booking = booking;
        this.snackBar.open(successMessage, 'Close', { duration: 3000 });
        this.loadJob(this.job!.id);
      },
      error: (err) => {
        this.isWorkingBooking = false;
        this.snackBar.open(err.error?.message || 'That action failed.', 'Close', { duration: 5000 });
      }
    });
  }

  // ---------------------------------------------------------------- review

  loadReview(jobId: number): void {
    this.reviewService.getJobReview(jobId).subscribe({
      next: (review) => (this.review = review),
      error: () => (this.review = null) // a 404 just means "not reviewed yet"
    });
  }

  /** The customer can review their own job, once, and only after it is complete. */
  canReview(): boolean {
    return !!this.job?.isOwner && this.job?.status === 'Completed' && !this.job?.hasReview;
  }

  setRating(rating: number): void {
    this.reviewForm.patchValue({ rating });
  }

  submitReview(): void {
    if (this.reviewForm.invalid || !this.job) {
      this.reviewForm.markAllAsTouched();
      this.snackBar.open('Pick a star rating first.', 'Close', { duration: 3000 });
      return;
    }

    this.isSubmittingReview = true;
    this.reviewService.createReview({
      jobId: this.job.id,
      rating: this.reviewForm.value.rating,
      comment: this.reviewForm.value.comment || undefined
    }).subscribe({
      next: (review) => {
        this.isSubmittingReview = false;
        this.review = review;
        this.snackBar.open('Thanks — your review is live.', 'Close', { duration: 4000 });
        this.loadJob(this.job!.id);
      },
      error: (err) => {
        this.isSubmittingReview = false;
        this.snackBar.open(err.error?.message || 'Failed to submit review.', 'Close', { duration: 5000 });
      }
    });
  }

  // ---------------------------------------------------------------- misc

  getStatusColor(status: string): string {
    switch (status) {
      case 'Open': return 'primary';
      case 'Bidding': return 'accent';
      case 'Assigned': return 'warn';
      case 'InProgress': return 'accent';
      case 'Completed': return 'primary';
      case 'Cancelled': return 'warn';
      default: return '';
    }
  }
}
