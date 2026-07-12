import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface UserProfile {
  id: string;
  email: string;
  fullName: string;
  phoneNumber: string;
  isVendor: boolean;
  role: string;
  createdAt: string;
}

export interface VendorProfile {
  id: string;
  companyName: string;
  description?: string;
  website?: string;
  address?: string;
  profilePictureUrl?: string;
  isVerified: boolean;
  averageRating: number;
  totalReviews: number;
  isAvailable: boolean;
  createdAt: string;
}

export interface CustomerProfile {
  id: string;
  address?: string;
  profilePictureUrl?: string;
  createdAt: string;
}

export interface FullProfileResponse {
  user: UserProfile;
  vendor?: VendorProfile;
  customer?: CustomerProfile;
}

export interface UpdateProfileRequest {
  fullName?: string;
  phoneNumber?: string;
  companyName?: string;
  description?: string;
  website?: string;
  address?: string;
  isAvailable?: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class ProfileService {
  private apiUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  getProfile(): Observable<FullProfileResponse> {
    return this.http.get<FullProfileResponse>(`${this.apiUrl}/Profile/me`);
  }

  updateProfile(data: UpdateProfileRequest): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/Profile`, data);
  }
}