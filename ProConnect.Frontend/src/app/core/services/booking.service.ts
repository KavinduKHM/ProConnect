import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface Booking {
  id: number;
  jobId: number;
  jobTitle: string;
  customerName: string;
  vendorName: string;
  vendorCompany: string;
  scheduledDate: string;
  startTime?: string;
  endTime?: string;
  status: string; // Scheduled | InProgress | Completed | Cancelled | NoShow
  totalPrice: number;
  isPaid: boolean;
  notes?: string;
  createdAt: string;
  completedAt?: string;
  hasReview: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class BookingService {
  private apiUrl = `${environment.apiUrl}/Bookings`;

  constructor(private http: HttpClient) {}

  /** Every booking the current user is party to, as customer or vendor. */
  getMyBookings(): Observable<Booking[]> {
    return this.http.get<Booking[]>(this.apiUrl);
  }

  getBooking(id: number): Observable<Booking> {
    return this.http.get<Booking>(`${this.apiUrl}/${id}`);
  }

  /** Vendor marks the work as under way. */
  start(id: number): Observable<Booking> {
    return this.http.post<Booking>(`${this.apiUrl}/${id}/start`, {});
  }

  /**
   * Vendor marks the work as done — this is what unlocks the customer's review.
   * An optional "after" photo gets compared against the customer's original by the AI.
   */
  complete(id: number, completionPhoto?: File | null): Observable<Booking> {
    const formData = new FormData();
    if (completionPhoto) {
      formData.append('completionPhoto', completionPhoto);
    }
    return this.http.post<Booking>(`${this.apiUrl}/${id}/complete`, formData);
  }

  cancel(id: number): Observable<Booking> {
    return this.http.post<Booking>(`${this.apiUrl}/${id}/cancel`, {});
  }
}
