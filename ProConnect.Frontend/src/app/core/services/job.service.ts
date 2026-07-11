import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface Job {
  id: number;
  title: string;
  description: string;
  imageUrl?: string;
  serviceCategoryName: string;
  customerName: string;
  location?: string;
  budgetMin: number;
  budgetMax: number;
  preferredDate: string;
  isUrgent: boolean;
  status: string;
  createdAt: string;
  assignedVendorName?: string;
  bidCount: number;
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

@Injectable({
  providedIn: 'root'
})
export class JobService {
  private apiUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  // Get all jobs (optional filters: status, categoryId)
  getJobs(status?: string, categoryId?: number): Observable<Job[]> {
    let url = `${this.apiUrl}/Jobs`;
    const params: string[] = [];
    if (status) params.push(`status=${status}`);
    if (categoryId) params.push(`categoryId=${categoryId}`);
    if (params.length) url += `?${params.join('&')}`;
    return this.http.get<Job[]>(url);
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
}