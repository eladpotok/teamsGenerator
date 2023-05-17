import { Button, Form, Input, InputNumber, Slider, message } from "antd";
import { useContext, useState } from "react";
import { v4 as uuidv4 } from 'uuid';
import { PlayersContext } from "../../Store/PlayersContext";
import AppButton from "./AppButton";
import { AnalyticsContext } from "../../Store/AnalyticsContext";
import { UserContext } from "../../Store/UserContext";
import { FrownOutlined, SmileOutlined, UserAddOutlined, UserOutlined } from "@ant-design/icons";
import { RxSlider } from 'react-icons/rx';
import { Radar } from 'react-chartjs-2';
import "./AddPlayerForm.css";

import {
    Chart as ChartJS,
    RadialLinearScale,
    PointElement,
    LineElement,
    Filler,
    Tooltip,
    Legend,
} from 'chart.js';
import AppSlider from "./AppSlider";
import { useEffect } from "react";

ChartJS.register(
    RadialLinearScale,
    PointElement,
    LineElement,
    Filler,
    Tooltip,
    Legend
);


function AddPlayerForm(props) {
    const [messageApi, contextHolder] = message.useMessage();

    const propertiesOfNumbers = props.playersProperties.filter(f => f.showInClient && f.type == "number")
    const propertiesForInput = props.playersProperties.filter(f => f.showInClient)

    const [propertiesChartData, setPropertiesChartData] = useState({ labels: [], datasets: [] })
    const [chartUpdated, setChartUpdated] = useState(false)
    
    useEffect(() => {
        (() => {
            if (props.player && props.playersProperties) {
                let data = {
                    labels: propertiesOfNumbers.map((pl) => { return pl.name }),
                    datasets: [
                        {
                            label: 'Rank',
                            data: getPlayerValuesForChart(),
                            backgroundColor: '#95edad',
                            borderColor: 'green',
                            borderWidth: 1,
                        },
                    ],
                };
                setPropertiesChartData(data)
            }
        })()
    }, [chartUpdated])

    function addPlayerHandler() {
        let isValid = true
        propertiesForInput.forEach(element => {
            if (props.player[element.name] == null || props.player[element.name] == undefined || props.player[element.name] == '') {
                isValid = false
                return
            }
        });

        if(isValid) {
            props.player['key'] =  props.player.key ? props.player.key : uuidv4();
            props.player['isArrived'] = false
            props.onPlayerSubmitted()
        }
        else{
            messageApi.open({
                type: 'error',
                content: 'One of the following input is wrong or missing',
              });
        }
    }

    function onResetClickedHandler() {
        setChartUpdated(!chartUpdated)
        props.onResetClicked()
    }

    function onValueChanged(value, key) {
        props.player[key] = value
        setChartUpdated(!chartUpdated)
    }

    function getPlayerValuesForChart() {
        return propertiesOfNumbers.map((pl) => {
            return props.player[pl.name]
        })
    }


    return (
        <div style={{ backgroundColor: 'white', borderRadius: '14px', padding: '5px' }}>
            {contextHolder}
            {props.playersProperties && props.playersProperties.filter(f => f.showInClient).map((pl) => {
                return (<>
                    {props.player && <div style={{ color: '#095c1f', fontWeight: 'bold', marginTop: '14px' }}>

                        {pl.type == 'number' && <div>

                            <RxSlider size={14} style={{color: '#095c1f', marginBottom: '-2px', marginRight: '4px'}} /><label>{pl.name.toUpperCase()}</label>
                            {props.player && <AppSlider onChanged={(value) => { onValueChanged(value, pl.name) }} value={props.player} displayPath={pl.name} />}

                        </div>}
                        {pl.type == 'text' && <Input  prefix={<UserOutlined />}
                            placeholder={pl.name.toUpperCase()} value={props.player[pl.name]} onChange={(e) => { onValueChanged(e.target.value, pl.name) }} />}
                    </div>}
                    </>)
            }
            )}
            {propertiesOfNumbers.length > 1 && <div style={{ height: '200px', marginTop: '40px', display: 'flex', justifyContent: 'center' }}>
                <Radar data={propertiesChartData} />
            </div>}

            <div style={{ display: 'flex', 'flex-direction': 'row', 'align-items': 'flex-end', 'justifyContent': 'flex-end' }}>
                {props.onResetClicked && <AppButton secondary htmlType="button" onClick={onResetClickedHandler}>
                    RESET
                </AppButton>}
                
                <AppButton onClick={addPlayerHandler} style={{ marginRight: 16 }}>
                    SUBMIT
                </AppButton>
            </div>
        </div>
    )
}

export default AddPlayerForm;