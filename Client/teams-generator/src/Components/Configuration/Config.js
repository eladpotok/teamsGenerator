import { Card, DatePicker, Form, Input, Select } from "antd";
import { QuestionCircleOutlined } from "@ant-design/icons";
import { useContext, useEffect, useRef, useState } from "react";
import { ConfigurationContext } from "../../Store/ConfigurationContext";
import dayjs from 'dayjs';
import { BrowserView, MobileView } from "react-device-detect";
import { AnalyticsContext } from "../../Store/AnalyticsContext";
import { UserContext } from "../../Store/UserContext";
import AppSlider from "../Common/AppSlider";
import { takeNElementsFromDic } from "../../Utilities/Helpers";
import './Config.css'
import { FaTshirt } from 'react-icons/fa';
import { RxSlider } from "react-icons/rx";
import { BsFillCalendar2DateFill, BsFillCalendarDateFill } from "react-icons/bs";
import AppButton from "../Common/AppButton";
import AppSelect from "../Common/AppSelect";

const { Option } = Select;

function Config(props) {
    const analyticsContext = useContext(AnalyticsContext)
    const userContext = useContext(UserContext)
    const configContext = useContext(ConfigurationContext);

    analyticsContext.sendAnalyticsImpression(userContext.user.uid, 'config')

    const [selectedEventDate, setSelectedEventDate] = useState(null)


    useEffect(() => {
        (  () => {
            setSelectedEventDate(props.initiatedConfig.eventDate)
        })()
    })


    function teamsNumberChangedHandler(value) {
        props.initiatedConfig['numberOfTeams'] = value
    }

    function shirtsColorsChangedHandler(values) {
        props.initiatedConfig['shirtsColors'] = values
    }

    function eventDateChangedHandler(e, dateValue) {
        setSelectedEventDate(e)
        props.initiatedConfig['eventDate'] = e
    }


    function submitClickedHandler() {
        configContext.setUserConfig({
            eventDate: props.initiatedConfig['eventDate'],
            shirtsColors: props.initiatedConfig['shirtsColors'],
            numberOfTeams: props.initiatedConfig.numberOfTeams,
            algo: props.currentAlgo
        })

        props.onSubmitClicked()
    }

    return (

        <div style={{borderRadius: '14px', color: '#095c1f', fontWeight: 'bold'}}>
                <div style={{ marginTop: '24px' }}>
                    <RxSlider  style={{marginRight: '4px', marginBottom: '-1px'}} /><label >TEAMS NUMBER</label>
                    <AppSlider onChanged={teamsNumberChangedHandler} value={props.initiatedConfig} displayPath='numberOfTeams' minValue={3} maxValue={5} />
                </div>
                
 
                <div style={{ marginTop: '24px' }}>
                    <FaTshirt  style={{marginRight: '4px', marginBottom: '-1px'}}/><label>SHIRTS</label>

                    <div style={{marginTop: '4px'}}>
                        <AppSelect onChanged={shirtsColorsChangedHandler} options={props.optionalShirts} value={props.initiatedConfig} displayPath='shirtsColors'/>
                    </div>
                    
                </div>

                <div style={{ marginTop: '24px' }}>
                    <BsFillCalendar2DateFill  style={{marginRight: '4px', marginBottom: '-1px'}}/><label>EVENT TIME</label>

                     {props.initiatedConfig.eventDate && <div style={{marginTop: '4px'}}>
                        <DatePicker style={{width: '100%'}} onChange={eventDateChangedHandler} format="YYYY-MM-DD HH:mm:ss" showTime={{ defaultValue: dayjs(new Date())}}  value={dayjs(selectedEventDate)} />
                    </div>}
                    
                </div>


                <div style={{ marginTop: '14px', display: 'flex', 'flex-direction': 'row', 'align-items': 'flex-end', 'justifyContent': 'flex-end' }}>
                    <AppButton onClick={submitClickedHandler}>
                        SUBMIT
                    </AppButton>
                </div>
        </div>
    )

}


export default Config;

