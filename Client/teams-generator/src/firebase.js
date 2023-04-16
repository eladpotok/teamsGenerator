// Import the functions you need from the SDKs you need
import { initializeApp } from "firebase/app";
import { getAnalytics } from "firebase/analytics";
// TODO: Add SDKs for Firebase products that you want to use
// https://firebase.google.com/docs/web/setup#available-libraries

// Your web app's Firebase configuration
// For Firebase JS SDK v7.20.0 and later, measurementId is optional
const firebaseConfig = {
  apiKey: "AIzaSyAy7Qf630Cq4vpJkR9-3a1zPMYOJYSyoV4",
  authDomain: "teamsgenratorapp.firebaseeapp.com",
  databaseURL: "https://teamsgeneratorapp-default-rtdb.firebaseio.com",
  projectId: "teamsgeneratorapp",
  storageBucket: "teamsgeneratorapp.appspot.com",
  messagingSenderId: "810692734476",
  appId: "1:810692734476:web:9f8497fe0ab8e5b727c829",
  measurementId: "G-8TP0D0S66M"
};

// Initialize Firebase
const app = initializeApp(firebaseConfig);
const analytics = getAnalytics(app);