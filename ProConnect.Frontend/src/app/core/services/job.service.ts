import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { VendorMatch, BidRanking } from './ai.service';

export interface Job {
  id: number;
  title: string;
  description: string;
  imageUrl?: string;
  serviceCategoryName: string;
  serviceCategoryId: number;
  customerName: string;
  location?: string;
  budgetMin: number;
  budgetMax: number;
  preferredDate: string;
  isUrgent: boolean;
  status: string;
  createdAt: string;
  completedAt?: string;
  assignedVendorName?: string;
  assignedVendorCompany?: string;
  bidCount: number;
  // Resolved by the API against the caller — the client never guesses ownership.
  isOwner: boolean;
  isAssignedVendor: boolean;
  bookingId?: number;
  bookingStatus?: string;
  hasReview: boolean;
  // Present when the post was written in another language and translated for vendors.
  originalDescription?: string;
  originalLanguage?: string;
  // The vendor's proof-of-completion photo and the AI's read of it.
  completionImageUrl?: string;
  completionVerdict?: string;
}

export interface CreateJobRequest {
  title: string;
  description: string;
  imageUrl?: string;
  serviceCategoryId: number;
  location?: string;
  budgetMin: number;
  budgetMax: number;
  preferredDate: string;
  isUrgent: boolean;
}

export interface Bid {
  id: number;
  bidAmount: number;
  proposalMessage?: string;
  estimatedDays: number;
  status: string;
  vendorName: string;
  vendorCompany: string;
  createdAt: string;
}

export interface CreateBidRequest {
  bidAmount: number;
  proposalMessage?: string;
  estimatedDays: number;
}

export interface JobFilter {
  search?: string;
  /** Match the search by meaning rather than keyword, so "water damage" finds "burst pipe". */
  semantic?: boolean;
  categoryId?: number;
  status?: string;
  isUrgent?: boolean;
  minBudget?: number;
  maxBudget?: number;
  location?: string;
  sortBy?: string;
  page?: number;
  pageSize?: number;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

@Injectable({
  providedIn: 'root'
})
export class JobService {
  private apiUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  // Browse the job board with filters, sorting and paging
  getJobs(filter: JobFilter = {}): Observable<PagedResult<Job>> {
    let params = new HttpParams();
    Object.entries(filter).forEach(([key, value]) => {
      if (value !== null && value !== undefined && value !== '') {
        params = params.set(key, String(value));
      }
    });
    return this.http.get<PagedResult<Job>>(`${this.apiUrl}/Jobs`, { params });
  }

  // Get single job by ID
  getJob(id: number): Observable<Job> {
    return this.http.get<Job>(`${this.apiUrl}/Jobs/${id}`);
  }

  // Create a new job (Customer only)
  createJob(job: CreateJobRequest): Observable<Job> {
    return this.http.post<Job>(`${this.apiUrl}/Jobs`, job);
  }

  // Place a bid on a job (Vendor only)
  placeBid(jobId: number, bid: CreateBidRequest): Observable<{ message: string; bidId: number }> {
    return this.http.post<{ message: string; bidId: number }>(`${this.apiUrl}/Jobs/${jobId}/bids`, bid);
  }

  // Get bids for a job (Customer owner only)
  getBids(jobId: number): Observable<Bid[]> {
    return this.http.get<Bid[]>(`${this.apiUrl}/Jobs/${jobId}/bids`);
  }

  // Accept a bid — this also opens the booking
  acceptBid(jobId: number, bidId: number): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/Jobs/${jobId}/bids/${bidId}/accept`, {});
  }

  // Reject a bid
  rejectBid(jobId: number, bidId: number): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/Jobs/${jobId}/bids/${bidId}/reject`, {});
  }

  // Get jobs posted by the current customer
  getMyJobs(): Observable<Job[]> {
    return this.http.get<Job[]>(`${this.apiUrl}/Jobs/my-jobs`);
  }

  // Get bids placed by the current vendor
  getMyBids(): Observable<any[]> {
    return this.http.get<any[]>(`${this.apiUrl}/Jobs/my-bids`);
  }

  // Get jobs assigned to the current vendor
  getAssignedJobs(): Observable<Job[]> {
    return this.http.get<Job[]>(`${this.apiUrl}/Jobs/assigned`);
  }

  /** The vendors the AI thinks fit this job (job owner only). */
  getRecommendedVendors(jobId: number): Observable<VendorMatch[]> {
    return this.http.get<VendorMatch[]>(`${this.apiUrl}/Jobs/${jobId}/recommended-vendors`);
  }

  /** Weighs the open bids on price, timeline, proposal and vendor reputation. */
  rankBids(jobId: number): Observable<BidRanking> {
    return this.http.get<BidRanking>(`${this.apiUrl}/Jobs/${jobId}/rank-bids`);
  }

  /** Turns the stored "/uploads/x.png" path into a URL the browser can load. */
  imageUrl(job: Job): string | null {
    return this.toAbsolute(job.imageUrl);
  }

  /** Same, for the vendor's proof-of-completion photo. */
  completionImageUrl(job: Job): string | null {
    return this.toAbsolute(job.completionImageUrl);
  }

  private toAbsolute(path?: string): string | null {
    if (!path) return null;
    return path.startsWith('http') ? path : `${environment.serverUrl}${path}`;
  }
}
