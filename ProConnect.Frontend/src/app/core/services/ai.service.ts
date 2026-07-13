import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface ImageAnalysis {
  title?: string;
  description?: string;
  suggestedCategory?: string;
  isUrgent: boolean;
  estimatedBudgetMin?: number;
  estimatedBudgetMax?: number;
  /** False when the photo shows no home-service issue — don't auto-fill the form from it. */
  isRelevant: boolean;
  /** Where the photo was stored — pass this straight back as the job's imageUrl. */
  imageUrl?: string;
}

export interface BidEvaluation {
  verdict: 'TooLow' | 'Fair' | 'TooHigh';
  reason: string;
  suggestedMin?: number;
  suggestedMax?: number;
}

export interface BudgetEstimate {
  estimatedMin: number;
  estimatedMax: number;
  rationale: string;
  /** How many real completed jobs backed the estimate. 0 means the AI had no history to lean on. */
  basedOnJobs: number;
}

export interface VendorMatch {
  vendorProfileId: string;
  companyName: string;
  skills?: string;
  averageRating: number;
  totalReviews: number;
  reputationSummary?: string;
  score: number;
  reason: string;
}

export interface RankedBid {
  bidId: number;
  rank: number;
  reason: string;
}

export interface BidRanking {
  recommendedBidId?: number;
  summary: string;
  ranking: RankedBid[];
}

@Injectable({
  providedIn: 'root'
})
export class AiService {
  private apiUrl = `${environment.apiUrl}/Ai`;

  constructor(private http: HttpClient) {}

  analyzeImage(file: File): Observable<ImageAnalysis> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<ImageAnalysis>(`${this.apiUrl}/analyze-image`, formData);
  }

  /** Rewrites a rough description into something a vendor can bid on. */
  improveDescription(description: string, title?: string, category?: string): Observable<{ improvedDescription: string }> {
    return this.http.post<{ improvedDescription: string }>(`${this.apiUrl}/improve-description`, {
      description,
      title,
      category
    });
  }

  /** Sanity-checks a bid against the job before the vendor commits to it. */
  evaluateBid(jobId: number, bidAmount: number, estimatedDays: number): Observable<BidEvaluation> {
    return this.http.post<BidEvaluation>(`${this.apiUrl}/evaluate-bid`, {
      jobId,
      bidAmount,
      estimatedDays
    });
  }

  /** Budget range anchored in what jobs like this actually completed for on the platform. */
  estimateBudget(description: string, serviceCategoryId: number, title?: string): Observable<BudgetEstimate> {
    return this.http.post<BudgetEstimate>(`${this.apiUrl}/estimate-budget`, {
      description,
      serviceCategoryId,
      title
    });
  }

  /** Drafts a vendor's proposal from their rough notes. */
  writeProposal(jobId: number, notes: string, bidAmount?: number, estimatedDays?: number): Observable<{ proposal: string }> {
    return this.http.post<{ proposal: string }>(`${this.apiUrl}/write-proposal`, {
      jobId,
      notes,
      bidAmount,
      estimatedDays
    });
  }
}
