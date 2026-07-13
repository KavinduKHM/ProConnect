import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface Review {
  id: number;
  jobId: number;
  jobTitle: string;
  reviewerName: string;
  vendorProfileId: string;
  vendorCompany: string;
  rating: number;
  comment?: string;
  createdAt: string;
}

export interface CreateReviewRequest {
  jobId: number;
  rating: number;
  comment?: string;
}

export interface VendorRating {
  vendorProfileId: string;
  companyName: string;
  averageRating: number;
  totalReviews: number;
  /** One-line AI blurb distilled from this vendor's reviews. */
  reputationSummary?: string;
  reviews: Review[];
}

@Injectable({
  providedIn: 'root'
})
export class ReviewService {
  private apiUrl = `${environment.apiUrl}/Reviews`;

  constructor(private http: HttpClient) {}

  createReview(review: CreateReviewRequest): Observable<Review> {
    return this.http.post<Review>(this.apiUrl, review);
  }

  getVendorRating(vendorProfileId: string): Observable<VendorRating> {
    return this.http.get<VendorRating>(`${this.apiUrl}/vendor/${vendorProfileId}`);
  }

  getJobReview(jobId: number): Observable<Review> {
    return this.http.get<Review>(`${this.apiUrl}/job/${jobId}`);
  }
}
